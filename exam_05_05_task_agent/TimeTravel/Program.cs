using System.Text;
using System.Text.Json;
using TimeTravel.Adapters;
using TimeTravel.Config;
using TimeTravel.Services;
using TimeTravel.Telemetry;
using TimeTravel.UI;

// Load .env file
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
        var sep = trimmed.IndexOf('=');
        if (sep > 0)
            Environment.SetEnvironmentVariable(trimmed[..sep], trimmed[(sep + 1)..]);
    }
}

var builder = WebApplication.CreateBuilder(args);

var agentConfig = new AgentConfig();
builder.Configuration.GetSection("Agent").Bind(agentConfig);

var hubConfig = new HubConfig();
builder.Configuration.GetSection("Hub").Bind(hubConfig);

var telemetryConfig = new TelemetryConfig();
builder.Configuration.GetSection("Telemetry").Bind(telemetryConfig);

var app = builder.Build();

using var telemetry = new TelemetrySetup(telemetryConfig);

var runLogger = new RunLogger();
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var hubApi = new HubApiClient(httpClient, hubConfig, runLogger);

ConsoleUI.PrintBanner("TimeTravel", "Time Machine — Fully Automated");
ConsoleUI.PrintInfo($"Hub: {hubConfig.ApiUrl}");
ConsoleUI.PrintInfo($"Log file: {runLogger.FilePath}");

if (string.IsNullOrWhiteSpace(hubConfig.ApiUrl) || string.IsNullOrWhiteSpace(hubConfig.ApiKey))
{
    ConsoleUI.PrintError("Hub__ApiUrl or Hub__ApiKey is empty. Check TimeTravel/.env");
    return;
}

await app.StartAsync();

// ─── Backend helpers ──────────────────────────────────────────────────────────

var backendUrl = $"{hubConfig.ApiUrl}/timetravel_backend";

async Task<JsonElement> PollDevice()
{
    var url = $"{backendUrl}?apikey={hubConfig.ApiKey}";
    var resp = await httpClient.GetStringAsync(url);
    runLogger.LogInfo($"POLL: {resp}");
    using var doc = JsonDocument.Parse(resp);
    return doc.RootElement.GetProperty("config").Clone();
}

async Task SetDevice(object fields)
{
    var payload = new Dictionary<string, object?> { ["apikey"] = hubConfig.ApiKey };
    var json = JsonSerializer.Serialize(fields);
    using var fieldsDoc = JsonDocument.Parse(json);
    foreach (var prop in fieldsDoc.RootElement.EnumerateObject())
    {
        payload[prop.Name] = prop.Value.ValueKind switch
        {
            JsonValueKind.String  => (object?)prop.Value.GetString(),
            JsonValueKind.Number when prop.Value.TryGetInt32(out var i) => i,
            JsonValueKind.Number  => prop.Value.GetDouble(),
            JsonValueKind.True    => true,
            JsonValueKind.False   => false,
            _                    => prop.Value.GetRawText()
        };
    }
    var bodyJson = JsonSerializer.Serialize(payload);
    ConsoleUI.PrintStep($"SetDevice: {bodyJson}");
    runLogger.LogInfo($"SET_DEVICE: {bodyJson}");
    var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
    var response = await httpClient.PostAsync(backendUrl, content);
    var responseBody = await response.Content.ReadAsStringAsync();
    ConsoleUI.PrintInfo($"SetDevice response: {responseBody}");
    runLogger.LogInfo($"SET_DEVICE_RESP: {responseBody}");
}

async Task<string> VerifyApi(object answer)
{
    var result = await hubApi.VerifyAsync(answer);
    return result;
}

// ─── Wait for jump conditions (fluxDensity=100, condition=stable, internalMode=target) ──
async Task WaitForConditions(int targetMode, string label)
{
    ConsoleUI.PrintStep($"Waiting for conditions: fluxDensity=100%, condition=stable, internalMode={targetMode} [{label}]");
    int attempts = 0;
    while (true)
    {
        attempts++;
        await Task.Delay(2000);
        var state = await PollDevice();
        var flux   = state.GetProperty("fluxDensity").GetInt32();
        var cond   = state.GetProperty("condition").GetString() ?? "";
        var iMode  = state.GetProperty("internalMode").GetInt32();
        var mode   = state.GetProperty("mode").GetString() ?? "";
        var battery = state.GetProperty("batteryStatus").GetString() ?? "";

        ConsoleUI.PrintInfo($"  [poll {attempts}] flux={flux}% cond={cond} iMode={iMode} mode={mode} battery={battery}");

        if (flux >= 100 && cond == "stable" && iMode == targetMode)
        {
            ConsoleUI.PrintInfo($"  Conditions met after {attempts} polls!");
            return;
        }

        if (attempts > 120) // 4 minutes timeout
        {
            ConsoleUI.PrintError($"  Timeout waiting for conditions (flux={flux}, cond={cond}, iMode={iMode})");
            return;
        }
    }
}

// ─── Execute a jump ──────────────────────────────────────────────────────────
async Task<string> ExecuteJump(int pwr, bool pta, bool ptb, int targetMode, string jumpLabel)
{
    ConsoleUI.PrintStep($"Setting up {jumpLabel}: PWR={pwr} PTA={pta} PTB={ptb} targetMode={targetMode}");

    // Set PWR and port switches
    await SetDevice(new { PWR = pwr, PTA = pta, PTB = ptb });
    await Task.Delay(1000);

    // Set mode to active
    await SetDevice(new { mode = "active" });
    await Task.Delay(1000);

    // Wait for all conditions
    await WaitForConditions(targetMode, jumpLabel);

    // Trigger the jump
    ConsoleUI.PrintStep($"Triggering jump: {jumpLabel}");
    var result = await VerifyApi(new { action = "timeTravel" });
    ConsoleUI.PrintInfo($"Jump result: {result}");
    runLogger.LogInfo($"JUMP_RESULT [{jumpLabel}]: {result}");

    // Switch back to standby
    await Task.Delay(1000);
    await SetDevice(new { mode = "standby" });

    return result;
}

// ─── Check for flag in a response string ────────────────────────────────────
string? FindFlag(string text)
{
    var idx = text.IndexOf("{{FLG:", StringComparison.OrdinalIgnoreCase);
    if (idx == -1) idx = text.IndexOf("{FLG:", StringComparison.OrdinalIgnoreCase);
    if (idx == -1) idx = text.IndexOf("FLG:", StringComparison.OrdinalIgnoreCase);
    if (idx < 0) return null;
    var end = text.IndexOf("}}", idx);
    if (end == -1) end = text.IndexOf("}", idx + 1);
    return end >= 0 ? text[idx..(end + 2)] : text[idx..Math.Min(idx + 80, text.Length)];
}

// ─── MISSION START ───────────────────────────────────────────────────────────

ConsoleUI.PrintStep("Step 0: Resetting device");
var resetResp = await VerifyApi(new { action = "reset" });
ConsoleUI.PrintInfo($"Reset: {resetResp}");
await Task.Delay(1000);

// ── JUMP 1: November 5, 2238 (future) ─────────────────────────────────────
ConsoleUI.PrintStep("Jump 1: Configuring for November 5, 2238 → future (PT-B, Mode 3, PWR=91)");
await VerifyApi(new { action = "configure", param = "year",          value = (object)2238 });
await VerifyApi(new { action = "configure", param = "month",         value = (object)11 });
await VerifyApi(new { action = "configure", param = "day",           value = (object)5 });
await VerifyApi(new { action = "configure", param = "syncRatio",     value = (object)0.82 });
await VerifyApi(new { action = "configure", param = "stabilization", value = (object)189 });

var cfg1 = await VerifyApi(new { action = "getConfig" });
ConsoleUI.PrintInfo($"Config before Jump 1: {cfg1}");

// Execute Jump 1: PT-B only (future), target internalMode=3 (years 2151-2300)
var jump1Result = await ExecuteJump(pwr: 91, pta: false, ptb: true, targetMode: 3, jumpLabel: "Jump1→2238");
var flag = FindFlag(jump1Result);
if (flag != null) { ConsoleUI.PrintResult($"FLAG FOUND after Jump 1: {flag}"); goto done; }

await Task.Delay(2000);
var postJump1 = await PollDevice();
ConsoleUI.PrintInfo($"Post-Jump 1 state: battery={postJump1.GetProperty("batteryStatus").GetString()}");

// ── JUMP 2: April 10, 2026 (return to today) ──────────────────────────────
ConsoleUI.PrintStep("Jump 2: Configuring for April 10, 2026 → past (PT-A, Mode 2, PWR=28)");
await VerifyApi(new { action = "configure", param = "year",          value = (object)2026 });
await VerifyApi(new { action = "configure", param = "month",         value = (object)4 });
await VerifyApi(new { action = "configure", param = "day",           value = (object)10 });
await VerifyApi(new { action = "configure", param = "syncRatio",     value = (object)0.69 });
await VerifyApi(new { action = "configure", param = "stabilization", value = (object)588 });

var cfg2 = await VerifyApi(new { action = "getConfig" });
ConsoleUI.PrintInfo($"Config before Jump 2: {cfg2}");

// Execute Jump 2: PT-A only (past), target internalMode=2 (years 2000-2150)
var jump2Result = await ExecuteJump(pwr: 28, pta: true, ptb: false, targetMode: 2, jumpLabel: "Jump2→2026");
flag = FindFlag(jump2Result);
if (flag != null) { ConsoleUI.PrintResult($"FLAG FOUND after Jump 2: {flag}"); goto done; }

await Task.Delay(2000);
var postJump2 = await PollDevice();
ConsoleUI.PrintInfo($"Post-Jump 2 state: battery={postJump2.GetProperty("batteryStatus").GetString()}");

// ── JUMP 3: November 12, 2024 — TIME PORTAL (tunnel) ──────────────────────
ConsoleUI.PrintStep("Jump 3: Configuring for November 12, 2024 → portal (PT-A+PT-B, Mode 2, PWR=19)");
await VerifyApi(new { action = "configure", param = "year",          value = (object)2024 });
await VerifyApi(new { action = "configure", param = "month",         value = (object)11 });
await VerifyApi(new { action = "configure", param = "day",           value = (object)12 });
await VerifyApi(new { action = "configure", param = "syncRatio",     value = (object)0.54 });
await VerifyApi(new { action = "configure", param = "stabilization", value = (object)995 });

var cfg3 = await VerifyApi(new { action = "getConfig" });
ConsoleUI.PrintInfo($"Config before Jump 3: {cfg3}");

// Execute Jump 3: PT-A + PT-B (portal/tunnel), target internalMode=2
var jump3Result = await ExecuteJump(pwr: 19, pta: true, ptb: true, targetMode: 2, jumpLabel: "Jump3→2024portal");
flag = FindFlag(jump3Result);
if (flag != null) { ConsoleUI.PrintResult($"FLAG FOUND after Jump 3: {flag}"); goto done; }

// ── FINAL: Scan all responses & getConfig ───────────────────────────────────
ConsoleUI.PrintStep("Final: checking getConfig for flag");
var finalConfig = await VerifyApi(new { action = "getConfig" });
ConsoleUI.PrintInfo($"Final config: {finalConfig}");
flag = FindFlag(finalConfig);
if (flag != null) { ConsoleUI.PrintResult($"FLAG FOUND in final config: {flag}"); goto done; }

// Try all jump results combined
var allText = string.Join("\n", resetResp, cfg1, jump1Result, cfg2, jump2Result, cfg3, jump3Result, finalConfig);
flag = FindFlag(allText);
if (flag != null)
{
    ConsoleUI.PrintResult($"FLAG FOUND: {flag}");
}
else
{
    ConsoleUI.PrintInfo("No flag found yet. Check the logs.");
    ConsoleUI.PrintInfo($"Jump 1 result: {jump1Result}");
    ConsoleUI.PrintInfo($"Jump 2 result: {jump2Result}");
    ConsoleUI.PrintInfo($"Jump 3 result: {jump3Result}");
}

done:
await app.StopAsync();
runLogger.Dispose();
