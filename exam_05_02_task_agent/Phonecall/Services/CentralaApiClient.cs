using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Phonecall.Config;
using Phonecall.UI;

namespace Phonecall.Services;

public class CentralaApiClient
{
    private static readonly ActivitySource Activity = new("Phonecall.Centrala");

    private readonly HttpClient _http;
    private readonly HubConfig _config;
    private readonly RunLogger _logger;

    private DateTimeOffset _nextAllowedCall = DateTimeOffset.MinValue;
    private const int FallbackDelayMs = 3000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public CentralaApiClient(HttpClient http, HubConfig config, RunLogger logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    /// <summary>Starts a new phonecall session. Returns raw API response JSON.</summary>
    public async Task<string> StartSessionAsync()
    {
        using var span = Activity.StartActivity("phonecall.start");
        _logger.LogInfo("Starting phonecall session...");
        ConsoleUI.PrintStep("Starting phonecall session");
        var result = await VerifyAsync(new { action = "start" });
        SetResponseTags(span, result);
        return result;
    }

    /// <summary>Sends an audio turn (base64 MP3). Returns raw API response JSON.</summary>
    public async Task<string> SendAudioAsync(string base64Audio)
    {
        using var span = Activity.StartActivity("phonecall.audio");
        _logger.LogInfo($"Sending audio turn ({base64Audio.Length} base64 chars)");
        ConsoleUI.PrintStep("Sending audio turn");
        var result = await VerifyAsync(new { audio = base64Audio });
        SetResponseTags(span, result);
        return result;
    }

    public async Task<string> VerifyAsync(object answer)
    {
        using var span = Activity.StartActivity("centrala.verify");
        var url = $"{_config.ApiUrl}/verify";
        ConsoleUI.PrintStep($"POST {url}");
        var result = await PostJsonAsync(url, new
        {
            apikey = _config.ApiKey,
            task = _config.TaskName,
            answer
        });
        SetResponseTags(span, result);
        return result;
    }

    public async Task<string> PostJsonAsync(string url, object body)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);

        // Truncate audio base64 in logs to keep them readable
        var logJson = json.Length > 2000
            ? json[..500] + $"...[truncated, total {json.Length} chars]..."
            : json;

        for (int attempt = 1; attempt <= _config.MaxRetries; attempt++)
        {
            await WaitForRateLimit();
            using var span = Activity.StartActivity("http.post");
            span?.SetTag("http.url", url);
            span?.SetTag("http.method", "POST");
            span?.SetTag("http.attempt", attempt);
            ConsoleUI.PrintApiRequest(attempt, _config.MaxRetries, logJson);
            _logger.LogApiRequest(url, logJson);

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage response;

            try
            {
                response = await _http.PostAsync(url, content);
            }
            catch (Exception ex)
            {
                span?.SetStatus(ActivityStatusCode.Error, ex.Message);
                ConsoleUI.PrintError($"Network error: {ex.Message}");
                _logger.LogNetworkError(url, ex.ToString());
                await DelayBeforeRetry(attempt);
                continue;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            span?.SetTag("http.status_code", (int)response.StatusCode);
            UpdateRateLimitState(response, responseBody);
            ConsoleUI.PrintApiResponse((int)response.StatusCode, responseBody);
            _logger.LogApiResponse((int)response.StatusCode, responseBody);

            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                ConsoleUI.PrintRetry("503 Service Unavailable - retrying...");
                await DelayBeforeRetry(attempt);
                continue;
            }

            if (response.StatusCode == (System.Net.HttpStatusCode)429)
            {
                ConsoleUI.PrintRetry("429 Rate limited - will auto-retry after waiting...");
                continue;
            }

            // Return raw JSON even for non-2xx so orchestrator can inspect
            // centrala error codes (e.g. -800) and extract embedded audio.
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInfo($"Non-2xx HTTP {(int)response.StatusCode} — returning raw body for orchestrator");
                return responseBody;
            }

            return responseBody;
        }

        return $"ERROR: All {_config.MaxRetries} attempts failed.";
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

        if (nextCall.HasValue)
            _nextAllowedCall = nextCall.Value;
    }

    private static void SetResponseTags(System.Diagnostics.Activity? span, string responseBody)
    {
        if (span == null) return;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (root.TryGetProperty("code", out var code))
                span.SetTag("centrala.code", code.GetInt32());
            if (root.TryGetProperty("message", out var msg))
                span.SetTag("centrala.message", msg.GetString());
        }
        catch { }
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
