using System.Diagnostics;
using System.Text.Json;
using WindPower.Models;
using WindPower.Tools;
using WindPower.UI;

namespace WindPower.Services;

/// <summary>
/// C#-driven orchestrator for the "windpower" task.
///
/// API actions (from help): help | get | config | start | done | getResult | unlockCodeGenerator
/// get params: weather | turbinecheck | powerplantcheck | documentation
/// getResult responses include a "sourceFunction" field for identification.
///
/// Timeline (40s after "start"):
///   Phase 0 (pre-timer) : help → get/documentation (synchronous turbine specs)
///   Phase 1             : start → fire get/weather + get/powerplantcheck in parallel
///                         → poll getResult until both collected (~0-10s)
///   Phase 2             : C# analysis — storm detection, recovery windows, production slot
///   Phase 3             : unlockCodeGenerator (parallel) → batch config
///                         → get/turbinecheck → done → flag
/// </summary>
public class WindpowerOrchestrator
{
    private readonly WindpowerTools _tools;
    private readonly RunLogger _logger;
    private readonly Stopwatch _timer = new();

    public WindpowerOrchestrator(WindpowerTools tools, RunLogger logger)
    {
        _tools = tools;
        _logger = logger;
    }

    // ── Entry point ────────────────────────────────────────────────────────────

    public async Task<string> RunAsync()
    {
        // ── Phase 0: Discovery (before timer) ─────────────────────────────────
        ConsoleUI.PrintPhase(0, "Discovery — help");
        _logger.LogPhase(0, "Discovery");

        var helpResult = await _tools.CallVerifyApi("help");
        _logger.LogInfo($"Help:\n{helpResult}");

        // ── Phase 1: Data Collection ───────────────────────────────────────────
        ConsoleUI.PrintPhase(1, "Data Collection");
        _logger.LogPhase(1, "Data Collection");

        var (weatherJson, powerJson, docsJson) = await Phase1_CollectDataAsync();

        // Parse turbine specs from documentation (after start — session is active)
        ConsoleUI.PrintInfo("Parsing turbine specs from documentation...");
        var turbineSpecs = ParseTurbineSpecs(docsJson);
        _logger.LogInfo($"Turbine specs: maxWind={turbineSpecs.MaxWindSpeed} cutIn={turbineSpecs.CutInSpeed} optimalPitch={turbineSpecs.OptimalPitchAngle}");
        ConsoleUI.PrintInfo($"Turbine: maxWind={turbineSpecs.MaxWindSpeed} m/s, cutIn={turbineSpecs.CutInSpeed} m/s, optimalPitch={turbineSpecs.OptimalPitchAngle}°");

        // ── Phase 2: Analysis ──────────────────────────────────────────────────
        ConsoleUI.PrintPhase(2, "Analysis");
        _logger.LogPhase(2, "Analysis");

        var configs = Phase2_BuildConfigs(weatherJson, powerJson, turbineSpecs);

        if (configs.Count == 0)
        {
            _logger.LogError("Phase2", "No config entries generated — check weather data in log.");
            ConsoleUI.PrintError("Analysis produced no config entries. Check log for raw API data.");
        }
        else
        {
            _logger.LogInfo($"Generated {configs.Count} config entries:");
            foreach (var c in configs)
                _logger.LogInfo($"  {c.DateTimeKey}  pitchAngle={c.PitchAngle}  mode={c.TurbineMode}  windMs={c.WindMs:F1}");
        }

        // ── Phase 3: Configuration ─────────────────────────────────────────────
        ConsoleUI.PrintPhase(3, "Configuration");
        _logger.LogPhase(3, "Configuration");

        return await Phase3_ConfigureAndFinalizeAsync(configs);
    }

    // ── Phase 1 ────────────────────────────────────────────────────────────────

    private async Task<(string Weather, string Power, string Docs)> Phase1_CollectDataAsync()
    {
        // Start the 40-second service window
        var startResult = await _tools.CallVerifyApi("start");
        _timer.Restart();
        _logger.LogInfo($"Service window opened. start: {startResult}");
        ConsoleUI.PrintTimer((int)(40 - _timer.Elapsed.TotalSeconds));

        // Fire all 3 data requests simultaneously (documentation requires active session)
        // NOTE: documentation is returned directly (synchronously), not via getResult
        ConsoleUI.PrintStep("Firing async: get/weather + get/powerplantcheck + get/documentation");
        var tWeather = _tools.CallVerifyApi("get", "{\"param\":\"weather\"}");
        var tPower = _tools.CallVerifyApi("get", "{\"param\":\"powerplantcheck\"}");
        var tDocs = _tools.CallVerifyApi("get", "{\"param\":\"documentation\"}");
        await Task.WhenAll(tWeather, tPower, tDocs);
        _logger.LogInfo($"Data requests queued at {_timer.Elapsed.TotalSeconds:F1}s");

        // Documentation is returned directly in the response (not via getResult)
        string? docsJson = tDocs.Result;
        if (!string.IsNullOrWhiteSpace(docsJson) && docsJson.Contains("cutoffWindMs"))
        {
            ConsoleUI.PrintInfo($"Got documentation directly ({docsJson.Length} chars)");
            _logger.LogInfo($"DOCS (direct):\n{docsJson}");
        }
        else
        {
            docsJson = null; // Will fall through to default specs
            _logger.LogInfo($"Documentation not in direct response, will look in getResult. Response: {Truncate(tDocs.Result, 100)}");
        }

        string? weatherJson = null;
        string? powerJson = null;
        const double dataDeadlineSec = 30.0;

        ConsoleUI.PrintStep("Polling getResult...");
        while (_timer.Elapsed.TotalSeconds < dataDeadlineSec &&
               (weatherJson == null || powerJson == null))
        {
            await Task.Delay(200);

            var result = await _tools.CallVerifyApi("getResult");
            _logger.LogInfo($"getResult [{_timer.Elapsed.TotalSeconds:F1}s]: {Truncate(result, 300)}");

            if (IsEmptyOrNotReady(result))
                continue;

            // Use sourceFunction field to identify the response type
            var source = GetSourceFunction(result);
            _logger.LogInfo($"  sourceFunction={source ?? "(none)"}");

            if (source == "weather" || (source == null && weatherJson == null && ContainsWeatherData(result)))
            {
                weatherJson = result;
                ConsoleUI.PrintInfo($"Got weather data ({result.Length} chars)");
                _logger.LogInfo($"WEATHER:\n{result}");
            }
            else if (source == "powerplantcheck" || (source == null && powerJson == null && ContainsPowerData(result)))
            {
                powerJson = result;
                ConsoleUI.PrintInfo($"Got power data ({result.Length} chars)");
                _logger.LogInfo($"POWER:\n{result}");
            }
            else
            {
                _logger.LogInfo($"Unclassified result (skipping): {result}");
            }

            ConsoleUI.PrintTimer((int)(40 - _timer.Elapsed.TotalSeconds));
        }

        _logger.LogInfo($"Phase 1 done at {_timer.Elapsed.TotalSeconds:F1}s. weather={weatherJson != null}, power={powerJson != null}, docs={docsJson != null}");
        return (weatherJson ?? "", powerJson ?? "", docsJson ?? tDocs.Result ?? "");
    }

    // ── Phase 2 ────────────────────────────────────────────────────────────────

    private List<ConfigEntry> Phase2_BuildConfigs(string weatherJson, string powerJson, TurbineSpecsData turbineSpecs)
    {
        double maxWindSpeed = turbineSpecs.MaxWindSpeed;
        double cutInSpeed = turbineSpecs.CutInSpeed;
        int optimalPitchAngle = turbineSpecs.OptimalPitchAngle;

        // Parse weather entries
        var entries = ParseWeatherEntries(weatherJson);
        if (entries.Count == 0)
        {
            _logger.LogError("Phase2", $"No weather entries. Raw: {weatherJson}");
            return new List<ConfigEntry>();
        }

        entries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        _logger.LogInfo($"Weather: {entries.Count} entries, {entries[0].Timestamp:dd.MM HH} → {entries[^1].Timestamp:dd.MM HH}");
        ConsoleUI.PrintInfo($"Weather entries: {entries.Count} hours");

        // ── Storm + production scheduling ─────────────────────────────────────
        //
        // State machine:
        //   turbineProtected = true  after we queue an idle config
        //   Turbine auto-resets ~1h after the last storm hour ends.
        //   recoveryEndsAt = first non-storm hour after a storm block + 1h
        //
        var configs = new List<ConfigEntry>();
        bool turbineProtected = false;
        bool inStorm = false;
        DateTime? recoveryEndsAt = null;
        bool productionAdded = false;

        foreach (var entry in entries)
        {
            bool isStorm = entry.WindSpeed > maxWindSpeed;

            if (isStorm)
            {
                if (!turbineProtected)
                {
                    configs.Add(new ConfigEntry(
                        StartDate: entry.Timestamp.ToString("yyyy-MM-dd"),
                        StartHour: entry.Timestamp.ToString("HH:00:00"),
                        PitchAngle: 90,
                        TurbineMode: "idle",
                        WindMs: entry.WindSpeed));
                    turbineProtected = true;
                    _logger.LogInfo($"Storm protection: {entry.Timestamp:dd.MM HH:mm} wind={entry.WindSpeed:F1}");
                    ConsoleUI.PrintInfo($"Storm {entry.Timestamp:dd.MM HH:mm} (wind={entry.WindSpeed:F1}) → idle/90°");
                }
                inStorm = true;
                // Extend reset time: 1h after THIS storm hour
                recoveryEndsAt = entry.Timestamp.AddHours(1);
            }
            else
            {
                if (inStorm)
                {
                    // Recovery ends 1h after the LAST storm entry (already set in the isStorm branch)
                    // Do NOT update recoveryEndsAt here — it was correctly set by the last storm entry
                    inStorm = false;
                }

                // Turbine auto-resets when recovery window has elapsed
                if (turbineProtected && recoveryEndsAt.HasValue && entry.Timestamp >= recoveryEndsAt.Value)
                {
                    turbineProtected = false;
                    _logger.LogInfo($"Turbine reset to normal at {entry.Timestamp:dd.MM HH:mm}");
                }

                if (!productionAdded && !turbineProtected)
                {
                    bool inRecovery = recoveryEndsAt.HasValue && entry.Timestamp < recoveryEndsAt.Value;
                    bool windOk = entry.WindSpeed >= cutInSpeed && entry.WindSpeed <= maxWindSpeed;

                    if (!inRecovery && windOk)
                    {
                        configs.Add(new ConfigEntry(
                            StartDate: entry.Timestamp.ToString("yyyy-MM-dd"),
                            StartHour: entry.Timestamp.ToString("HH:00:00"),
                            PitchAngle: optimalPitchAngle,
                            TurbineMode: "production",
                            WindMs: entry.WindSpeed));
                        productionAdded = true;
                        _logger.LogInfo($"Production: {entry.Timestamp:dd.MM HH:mm} wind={entry.WindSpeed:F1} pitch={optimalPitchAngle}");
                        ConsoleUI.PrintInfo($"Production {entry.Timestamp:dd.MM HH:mm} (wind={entry.WindSpeed:F1}) → production/{optimalPitchAngle}°");
                    }
                }
            }
        }

        if (!productionAdded)
            ConsoleUI.PrintError("No suitable production window found in forecast!");

        return configs;
    }

    // ── Phase 3 ────────────────────────────────────────────────────────────────

    private async Task<string> Phase3_ConfigureAndFinalizeAsync(List<ConfigEntry> configs)
    {
        if (configs.Count == 0)
        {
            // Still need turbinecheck before done
            await RunTurbineCheckAsync();
            return await _tools.CallVerifyApi("done");
        }

        // ── Request unlock codes in parallel ──────────────────────────────────
        ConsoleUI.PrintStep($"Requesting {configs.Count} unlock codes...");
        _logger.LogInfo($"Unlock code requests at {_timer.Elapsed.TotalSeconds:F1}s");

        var unlockTasks = configs.Select(c =>
            _tools.CallVerifyApi("unlockCodeGenerator", JsonSerializer.Serialize(new
            {
                startDate = c.StartDate,
                startHour = c.StartHour,
                windMs = c.WindMs,
                pitchAngle = c.PitchAngle
            }))).ToArray();

        await Task.WhenAll(unlockTasks);
        _logger.LogInfo($"Unlock requests queued at {_timer.Elapsed.TotalSeconds:F1}s");

        // ── Poll for unlock codes (sourceFunction = "unlockCodeGenerator") ─────
        // Match codes by startDate+startHour embedded in the getResult response
        var unlockCodesMap = new Dictionary<string, string>(); // key = "startDate startHour"
        const double unlockDeadlineSec = 34.0;

        ConsoleUI.PrintStep("Polling for unlock codes...");
        while (_timer.Elapsed.TotalSeconds < unlockDeadlineSec && unlockCodesMap.Count < configs.Count)
        {
            await Task.Delay(200);
            var result = await _tools.CallVerifyApi("getResult");
            _logger.LogInfo($"getResult (unlock) [{_timer.Elapsed.TotalSeconds:F1}s]: {Truncate(result, 300)}");

            if (IsEmptyOrNotReady(result))
                continue;

            var source = GetSourceFunction(result);
            if (source == "unlockCodeGenerator" || IsUnlockCodeResponse(result))
            {
                var code = ExtractUnlockCode(result);
                var codeKey = ExtractUnlockCodeKey(result); // "startDate startHour"
                if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(codeKey))
                {
                    unlockCodesMap[codeKey] = code;
                    ConsoleUI.PrintInfo($"Unlock code [{codeKey}] ({unlockCodesMap.Count}/{configs.Count}): {code}");
                    _logger.LogInfo($"Unlock code [{codeKey}]: {code}");
                }
                else if (!string.IsNullOrEmpty(code))
                {
                    _logger.LogInfo($"WARNING: Unlock code without key: {code} — result: {result}");
                }
            }
            else
            {
                _logger.LogInfo($"Non-unlock getResult (skipping): {result}");
            }

            ConsoleUI.PrintTimer((int)(40 - _timer.Elapsed.TotalSeconds));
        }

        // ── Send batch config ─────────────────────────────────────────────────
        ConsoleUI.PrintStep("Sending batch config...");
        var configsDict = new Dictionary<string, object>();
        for (int i = 0; i < configs.Count; i++)
        {
            var key = configs[i].DateTimeKey;
            if (!unlockCodesMap.TryGetValue(key, out var unlockCode))
            {
                unlockCode = "timeout-no-code";
                _logger.LogInfo($"WARNING: No unlock code for {key} — using placeholder.");
            }
            configsDict[key] = new
            {
                pitchAngle = configs[i].PitchAngle,
                turbineMode = configs[i].TurbineMode,
                unlockCode
            };
        }

        var configPayload = JsonSerializer.Serialize(new { configs = configsDict });
        var configResult = await _tools.CallVerifyApi("config", configPayload);
        _logger.LogInfo($"Config result at {_timer.Elapsed.TotalSeconds:F1}s: {configResult}");
        ConsoleUI.PrintInfo($"Config: {configResult}");

        // ── Turbine check (required before done, per API notes) ───────────────
        await RunTurbineCheckAsync();

        // ── Done → flag ───────────────────────────────────────────────────────
        ConsoleUI.PrintStep("Sending done...");
        var doneResult = await _tools.CallVerifyApi("done");
        _logger.LogInfo($"Done at {_timer.Elapsed.TotalSeconds:F1}s: {doneResult}");

        return doneResult;
    }

    private async Task RunTurbineCheckAsync()
    {
        ConsoleUI.PrintStep("Running turbinecheck (get/turbinecheck)...");
        _logger.LogInfo($"Turbinecheck at {_timer.Elapsed.TotalSeconds:F1}s");

        var tcResult = await _tools.CallVerifyApi("get", "{\"param\":\"turbinecheck\"}");
        _logger.LogInfo($"Turbinecheck queue ack: {tcResult}");

        // Poll for turbinecheck result (sourceFunction = "turbinecheck")
        const double tcDeadlineSec = 38.5;
        while (_timer.Elapsed.TotalSeconds < tcDeadlineSec)
        {
            await Task.Delay(300);
            var r = await _tools.CallVerifyApi("getResult");
            _logger.LogInfo($"getResult (tc) [{_timer.Elapsed.TotalSeconds:F1}s]: {Truncate(r, 300)}");

            if (IsEmptyOrNotReady(r))
                continue;

            var src = GetSourceFunction(r);
            if (src == "turbinecheck")
            {
                ConsoleUI.PrintInfo($"Turbinecheck result: {Truncate(r, 200)}");
                _logger.LogInfo($"Turbinecheck result: {r}");
                return;
            }

            _logger.LogInfo($"Non-turbinecheck getResult (skipping): {r}");
        }

        _logger.LogInfo("Turbinecheck timeout — proceeding to done.");
    }

    // ── Documentation / turbine spec parsing ──────────────────────────────────

    private TurbineSpecsData ParseTurbineSpecs(string docsJson)
    {
        double maxWind = 14.0;  // default: safety.cutoffWindMs
        double cutIn = 4.0;     // default: safety.minOperationalWindMs
        int optPitch = 0;       // default: pitch 0 = 100% yield (from pitchAngleYieldPercent)

        if (string.IsNullOrWhiteSpace(docsJson)) return new TurbineSpecsData(maxWind, cutIn, optPitch);

        try
        {
            using var doc = JsonDocument.Parse(docsJson);
            var root = doc.RootElement;

            // Primary: look in safety section (actual API structure)
            if (root.TryGetProperty("safety", out var safety))
            {
                maxWind = FindDouble(safety, "cutoffWindMs", "maxWindMs", "cutOutWindMs") ?? maxWind;
                cutIn = FindDouble(safety, "minOperationalWindMs", "cutInWindMs", "minWindMs") ?? cutIn;
            }

            // Fallback: flat fields
            maxWind = FindDouble(root, "maxWindSpeed", "maxWind", "cutOutSpeed", "cutoffWindMs") ?? maxWind;
            cutIn = FindDouble(root, "cutInSpeed", "cutIn", "minWindSpeed", "minOperationalWindMs") ?? cutIn;

            // optimalPitchAngle: use 0 (100% yield) unless explicitly specified
            optPitch = (int)(FindDouble(root, "optimalPitchAngle", "nominalPitchAngle") ?? optPitch);
        }
        catch (Exception ex)
        {
            _logger.LogError("ParseDocs", $"{ex.Message}\nRaw: {docsJson}");
        }

        return new TurbineSpecsData(maxWind, cutIn, optPitch);
    }

    // ── Response classification helpers ───────────────────────────────────────

    private static bool IsEmptyOrNotReady(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return true;
        var lower = response.ToLowerInvariant();

        // Explicit API code for "not ready yet": code 11
        if (lower.Contains("\"code\":11") || lower.Contains("\"code\": 11")) return true;

        return lower.Contains("no completed") ||
               lower.Contains("no result") ||
               lower.Contains("not ready") ||
               lower.Contains("queue is empty") ||
               lower.Contains("empty") && lower.Contains("queue") ||
               response.Trim() is "{}" or "null";
    }

    private static string? GetSourceFunction(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (root.TryGetProperty("sourceFunction", out var sf) && sf.ValueKind == JsonValueKind.String)
                return sf.GetString();
        }
        catch { }
        return null;
    }

    private static bool ContainsWeatherData(string r)
    {
        var l = r.ToLowerInvariant();
        return (l.Contains("wind") || l.Contains("forecast") || l.Contains("weather")) &&
               (l.Contains("speed") || l.Contains("date") || l.Contains("hour"));
    }

    private static bool ContainsPowerData(string r)
    {
        var l = r.ToLowerInvariant();
        return l.Contains("power") || l.Contains("watt") || l.Contains("kw") ||
               l.Contains("demand") || l.Contains("shortage") || l.Contains("required");
    }

    private static bool ContainsDocumentationData(string r)
    {
        var l = r.ToLowerInvariant();
        return l.Contains("maxwindspeed") || l.Contains("cutinspeed") || l.Contains("pitchangle") ||
               l.Contains("turbinespec") || l.Contains("specification") || l.Contains("documentation");
    }

    private static bool IsUnlockCodeResponse(string r)
    {
        var l = r.ToLowerInvariant();
        return l.Contains("unlockcode") || l.Contains("unlock_code") || l.Contains("code") && l.Contains("sign");
    }

    /// <summary>Extracts "startDate startHour" key from an unlockCodeGenerator result to match it to a config entry.</summary>
    private static string ExtractUnlockCodeKey(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // Try root-level first
            if (root.TryGetProperty("startDate", out var sd) && root.TryGetProperty("startHour", out var sh))
                return $"{sd.GetString()} {sh.GetString()}";

            // Try nested in "signedParams"
            if (root.TryGetProperty("signedParams", out var sp))
            {
                if (sp.TryGetProperty("startDate", out var sd2) && sp.TryGetProperty("startHour", out var sh2))
                    return $"{sd2.GetString()} {sh2.GetString()}";
            }
        }
        catch { }
        return "";
    }

    private static string ExtractUnlockCode(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // Nested result: {"sourceFunction":"unlockCodeGenerator","result":"<code>"}
            if (root.TryGetProperty("result", out var resultProp))
            {
                if (resultProp.ValueKind == JsonValueKind.String)
                    return resultProp.GetString() ?? "";
                // result might be an object with a code field
                if (resultProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var key in new[] { "code", "unlockCode", "token", "signature" })
                        if (resultProp.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                            return v.GetString() ?? "";
                }
            }

            // Flat: {"code":"...", "unlockCode":"..."}
            foreach (var key in new[] { "unlockCode", "unlock_code", "token", "signature", "hash" })
                if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString() ?? "";

            // Plain string root
            if (root.ValueKind == JsonValueKind.String)
                return root.GetString() ?? "";
        }
        catch { }
        return "";
    }

    // ── JSON parsing helpers ───────────────────────────────────────────────────

    private static double? FindDouble(JsonElement element, params string[] names)
    {
        // Search in the element and one level of nesting
        var result = TryGetDoubleFlat(element, names);
        if (result.HasValue) return result;

        // Search nested objects
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    var nested = TryGetDoubleFlat(prop.Value, names);
                    if (nested.HasValue) return nested;
                }
            }
        }
        return null;
    }

    private static double? TryGetDoubleFlat(JsonElement el, string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number) return prop.GetDouble();
                if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out var d)) return d;
            }
        }
        return null;
    }

    private List<WeatherEntry> ParseWeatherEntries(string json)
    {
        var entries = new List<WeatherEntry>();
        if (string.IsNullOrWhiteSpace(json)) return entries;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Find array — may be root or nested in "result", "data", "forecast", etc.
            JsonElement? arr = null;
            if (root.ValueKind == JsonValueKind.Array)
            {
                arr = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "result", "data", "forecast", "weather", "entries", "hours", "records" })
                {
                    if (root.TryGetProperty(key, out var inner) && inner.ValueKind == JsonValueKind.Array)
                    { arr = inner; break; }
                }
                if (arr == null)
                    foreach (var prop in root.EnumerateObject())
                        if (prop.Value.ValueKind == JsonValueKind.Array) { arr = prop.Value; break; }
            }

            if (arr == null)
            {
                _logger.LogError("ParseWeather", $"No array found in: {json}");
                return entries;
            }

            foreach (var item in arr.Value.EnumerateArray())
            {
                var ts = ParseTimestamp(item);
                var wind = TryGetDoubleFlat(item, new[] {
                    "windSpeed", "wind_speed", "wind", "windSpeedMs", "speed",
                    "windMs", "wind_ms", "predkoscWiatru" }) ?? 0;

                if (ts.HasValue)
                    entries.Add(new WeatherEntry(ts.Value, wind));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("ParseWeather", $"{ex.Message}\nRaw: {json}");
        }

        _logger.LogInfo($"Parsed {entries.Count} weather entries.");
        return entries;
    }

    private static DateTime? ParseTimestamp(JsonElement item)
    {
        // Combined datetime field
        foreach (var f in new[] { "datetime", "date_time", "timestamp", "time", "dateTime" })
        {
            if (item.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(v.GetString(), out var dt))
                return dt;
        }

        // date + hour
        string? dateStr = null;
        int? hourInt = null;

        foreach (var f in new[] { "date", "day", "data" })
            if (item.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.String)
            { dateStr = v.GetString(); break; }

        foreach (var f in new[] { "hour", "godzina", "h", "hours" })
        {
            if (item.TryGetProperty(f, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number) { hourInt = v.GetInt32(); break; }
                if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var i)) { hourInt = i; break; }
            }
        }

        if (dateStr != null && hourInt.HasValue && DateTime.TryParse(dateStr, out var d))
            return d.Date.AddHours(hourInt.Value);

        return null;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}
