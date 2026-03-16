using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpkAgent.Config;
using SpkAgent.UI;

namespace SpkAgent.Services;

public class HubApiClient
{
    private static readonly ActivitySource Activity = new("SpkAgent.Hub");

    private readonly HttpClient _http;
    private readonly HubConfig _config;

    private DateTimeOffset _nextAllowedCall = DateTimeOffset.MinValue;
    private const int FallbackDelayMs = 3000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public HubApiClient(HttpClient http, HubConfig config)
    {
        _http = http;
        _config = config;
    }

    public async Task<string> SubmitDeclarationAsync(string declaration)
    {
        using var span = Activity.StartActivity("hub.submit_declaration");
        span?.SetTag("declaration.length", declaration.Length);

        var body = new
        {
            apikey = _config.ApiKey,
            task = _config.TaskName,
            answer = new { declaration }
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);

        for (int attempt = 1; attempt <= _config.MaxRetries; attempt++)
        {
            await WaitForRateLimit();

            using var httpSpan = Activity.StartActivity("http.post");
            httpSpan?.SetTag("http.url", _config.ApiUrl);
            httpSpan?.SetTag("http.method", "POST");
            httpSpan?.SetTag("http.attempt", attempt);
            httpSpan?.SetTag("http.request.body", json);

            ConsoleUI.PrintApiRequest(attempt, _config.MaxRetries, json);

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage response;

            try
            {
                response = await _http.PostAsync(_config.ApiUrl, content);
            }
            catch (Exception ex)
            {
                httpSpan?.SetStatus(ActivityStatusCode.Error, ex.Message);
                ConsoleUI.PrintError($"Network error: {ex.Message}");
                await DelayBeforeRetry(attempt);
                continue;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            httpSpan?.SetTag("http.status_code", (int)response.StatusCode);
            httpSpan?.SetTag("http.response.body", responseBody);
            UpdateRateLimitState(response, responseBody);
            ConsoleUI.PrintApiResponse((int)response.StatusCode, responseBody);

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

            if (!response.IsSuccessStatusCode)
            {
                span?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)response.StatusCode}");
                return $"HTTP {(int)response.StatusCode}: {responseBody}";
            }

            SetResponseTags(span, responseBody);
            return responseBody;
        }

        span?.SetStatus(ActivityStatusCode.Error, "All attempts failed");
        return $"ERROR: All {_config.MaxRetries} attempts failed.";
    }

    private static void SetResponseTags(System.Diagnostics.Activity? span, string responseBody)
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
