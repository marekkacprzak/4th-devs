using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FirmwareAgent.Config;
using FirmwareAgent.UI;

namespace FirmwareAgent.Services;

public class HubApiClient
{
    private static readonly ActivitySource Activity = new("FirmwareAgent.Hub");

    private readonly HttpClient _http;
    private readonly HubConfig _config;
    private readonly FileLogger? _logger;

    private DateTimeOffset _nextAllowedCall = DateTimeOffset.MinValue;
    private const int FallbackDelayMs = 3000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public HubApiClient(HttpClient http, HubConfig config, FileLogger? logger = null)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<string> ExecuteShellCommandAsync(string command)
    {
        using var span = Activity.StartActivity("hub.shell_command");
        span?.SetTag("shell.command", command);

        var body = new { apikey = _config.ApiKey, cmd = command };
        var url = $"{_config.ApiUrl}/api/shell";

        _logger?.LogShellRequest(command);

        var result = await PostWithRetryAsync(url, body, span, "shell");
        return result;
    }

    public async Task<string> SubmitAnswerAsync(string confirmation)
    {
        using var span = Activity.StartActivity("hub.submit_answer");
        span?.SetTag("answer.confirmation", confirmation);

        var body = new
        {
            apikey = _config.ApiKey,
            task = _config.TaskName,
            answer = new { confirmation }
        };
        var url = $"{_config.ApiUrl}/verify";

        _logger?.LogSubmitRequest(confirmation);

        var result = await PostWithRetryAsync(url, body, span, "submit");
        return result;
    }

    private async Task<string> PostWithRetryAsync(string url, object body, Activity? span, string context)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);

        for (int attempt = 1; attempt <= _config.MaxRetries; attempt++)
        {
            await WaitForRateLimit();

            using var httpSpan = Activity.StartActivity("http.post");
            httpSpan?.SetTag("http.url", url);
            httpSpan?.SetTag("http.method", "POST");
            httpSpan?.SetTag("http.attempt", attempt);
            httpSpan?.SetTag("http.request.body", json);

            ConsoleUI.PrintApiRequest(attempt, _config.MaxRetries, json);

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage response;

            try
            {
                response = await _http.PostAsync(url, content);
            }
            catch (Exception ex)
            {
                httpSpan?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger?.LogError(context, ex.Message);
                ConsoleUI.PrintError($"Network error: {ex.Message}");
                await DelayBeforeRetry(attempt);
                continue;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            httpSpan?.SetTag("http.status_code", (int)response.StatusCode);
            httpSpan?.SetTag("http.response.body", responseBody);
            UpdateRateLimitState(response, responseBody);

            var statusCode = (int)response.StatusCode;
            ConsoleUI.PrintApiResponse(statusCode, responseBody);

            if (context == "shell")
                _logger?.LogShellResponse(statusCode, responseBody);
            else
                _logger?.LogSubmitResponse(statusCode, responseBody);

            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                ConsoleUI.PrintRetry("503 Service Unavailable - retrying...");
                await DelayBeforeRetry(attempt);
                continue;
            }

            if (response.StatusCode == (System.Net.HttpStatusCode)429)
            {
                ConsoleUI.PrintRetry("429 Rate limited - waiting before retry...");
                await DelayBeforeRetry(attempt);
                continue;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var banInfo = ExtractBanInfo(responseBody);
                if (banInfo.HasValue)
                {
                    var (waitSec, reason, rebooted) = banInfo.Value;
                    ConsoleUI.PrintRetry($"Security ban ({waitSec}s): {reason}");
                    await Task.Delay(TimeSpan.FromSeconds(waitSec + 2));
                    var rebootMsg = rebooted ? " VM has been rebooted to initial state — start fresh." : "";
                    span?.SetStatus(ActivityStatusCode.Error, "Security ban");
                    return $"BANNED: {reason} Waited {waitSec}s for ban to expire.{rebootMsg} " +
                           "IMPORTANT: Do NOT access .env files, /etc, /root, /proc, or any file listed in .gitignore. " +
                           "Always read .gitignore first before accessing files in a directory.";
                }
                span?.SetStatus(ActivityStatusCode.Error, "HTTP 403");
                return $"HTTP 403: {responseBody}";
            }

            if (!response.IsSuccessStatusCode)
            {
                span?.SetStatus(ActivityStatusCode.Error, $"HTTP {statusCode}");
                return $"HTTP {statusCode}: {responseBody}";
            }

            SetResponseTags(span, responseBody);
            return responseBody;
        }

        // Enforce a 60s cooldown after exhausting retries so the next call doesn't immediately hit rate limit
        _nextAllowedCall = DateTimeOffset.UtcNow.AddSeconds(60);
        ConsoleUI.PrintInfo("All retries exhausted. Enforcing 60s cooldown before next call.");
        span?.SetStatus(ActivityStatusCode.Error, "All attempts failed");
        return $"ERROR: All {_config.MaxRetries} attempts failed. Rate limited. Wait at least 60 seconds before the next command.";
    }

    private static (int waitSec, string reason, bool rebooted)? ExtractBanInfo(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var codeEl)) return null;
            var code = codeEl.GetInt32();
            if (code != -733 && code != -735) return null;

            if (!root.TryGetProperty("ban", out var banEl)) return null;

            int waitSec = 0;
            if (banEl.TryGetProperty("ttl_seconds", out var ttl))
                waitSec = ttl.GetInt32();
            else if (banEl.TryGetProperty("seconds_left", out var left))
                waitSec = left.GetInt32();

            string reason = "Security policy violation.";
            if (banEl.TryGetProperty("reason", out var reasonEl))
                reason = reasonEl.GetString() ?? reason;

            bool rebooted = root.TryGetProperty("reboot", out var rebootEl) && rebootEl.GetBoolean();

            return (waitSec, reason, rebooted);
        }
        catch { return null; }
    }

    private static void SetResponseTags(Activity? span, string responseBody)
    {
        if (span == null) return;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (root.TryGetProperty("code", out var code))
                span.SetTag("hub.code", code.GetInt32());
            if (root.TryGetProperty("message", out var msg))
                span.SetTag("hub.message", msg.GetString());
        }
        catch { }
    }

    private async Task WaitForRateLimit()
    {
        var now = DateTimeOffset.UtcNow;
        if (_nextAllowedCall > now)
        {
            var waitMs = (int)(_nextAllowedCall - now).TotalMilliseconds;
            ConsoleUI.PrintRateLimit(waitMs);
            await Task.Delay(waitMs);
        }
    }

    private void UpdateRateLimitState(HttpResponseMessage response, string responseBody)
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? nextCall = null;

        if (response.Headers.TryGetValues("retry-after", out var retryAfterValues))
        {
            if (int.TryParse(retryAfterValues.FirstOrDefault(), out int retryAfterSec))
                nextCall = now.AddSeconds(retryAfterSec).AddMilliseconds(500);
        }

        if (!nextCall.HasValue)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("retry_after", out var retryProp) &&
                    retryProp.TryGetInt32(out int retryAfterBody))
                    nextCall = now.AddSeconds(retryAfterBody).AddMilliseconds(500);
            }
            catch { }
        }

        if (!nextCall.HasValue && response.IsSuccessStatusCode)
            nextCall = now.AddMilliseconds(FallbackDelayMs);

        if (!nextCall.HasValue && response.StatusCode == (System.Net.HttpStatusCode)429)
            nextCall = now.AddSeconds(5);

        if (nextCall.HasValue)
        {
            _nextAllowedCall = nextCall.Value;
            var waitMs = (int)(nextCall.Value - now).TotalMilliseconds;
            ConsoleUI.PrintInfo($"Next call allowed in {waitMs}ms");
        }
    }

    private async Task DelayBeforeRetry(int attempt)
    {
        var delay = _config.RetryDelayMs * (int)Math.Pow(2, attempt - 1);
        delay = Math.Min(delay, 30_000);

        var now = DateTimeOffset.UtcNow;
        if (_nextAllowedCall > now)
        {
            var rateLimitDelay = (int)(_nextAllowedCall - now).TotalMilliseconds;
            if (rateLimitDelay > delay)
                delay = rateLimitDelay;
        }

        ConsoleUI.PrintInfo($"Waiting {delay}ms before retry...");
        await Task.Delay(delay);
    }
}
