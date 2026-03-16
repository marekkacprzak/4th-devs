using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RailwayAgent.Config;
using RailwayAgent.UI;

namespace RailwayAgent.Services;

public class RailwayApiClient
{
    private static readonly ActivitySource Activity = new("RailwayAgent.Hub");

    private readonly HttpClient _http;
    private readonly RailwayConfig _config;

    private DateTimeOffset _nextAllowedCall = DateTimeOffset.MinValue;
    private const int FallbackDelayMs = 5000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public RailwayApiClient(HttpClient http, RailwayConfig config)
    {
        _http = http;
        _config = config;
    }

    public async Task<string> SendAsync(Dictionary<string, string> answer)
    {
        var body = new
        {
            apikey = _config.ApiKey,
            task = _config.TaskName,
            answer
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);

        for (int attempt = 1; attempt <= _config.MaxRetries; attempt++)
        {
            // Wait for rate limit before sending
            await WaitForRateLimit();

            using var span = Activity.StartActivity("http.post");
            span?.SetTag("http.url", _config.ApiUrl);
            span?.SetTag("http.method", "POST");
            span?.SetTag("http.attempt", attempt);
            span?.SetTag("http.request.body", json);

            ConsoleUI.PrintApiRequest(attempt, _config.MaxRetries, json);

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage response;

            try
            {
                response = await _http.PostAsync(_config.ApiUrl, content);
            }
            catch (Exception ex)
            {
                span?.SetStatus(ActivityStatusCode.Error, ex.Message);
                ConsoleUI.PrintError($"Network error: {ex.Message}");
                await DelayBeforeRetry(attempt);
                continue;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            span?.SetTag("http.status_code", (int)response.StatusCode);
            span?.SetTag("http.response.body", responseBody);

            // Update rate limit state from headers and body
            UpdateRateLimitState(response, responseBody);

            ConsoleUI.PrintApiResponse((int)response.StatusCode, responseBody);

            // Auto-retry on 503 (simulated outage)
            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                ConsoleUI.PrintRetry("503 Service Unavailable - retrying...");
                await DelayBeforeRetry(attempt);
                continue;
            }

            // Auto-retry on 429 (rate limit) - don't return error to agent
            if (response.StatusCode == (System.Net.HttpStatusCode)429)
            {
                ConsoleUI.PrintRetry("429 Rate limited - will auto-retry after waiting...");
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                return $"HTTP {(int)response.StatusCode}: {responseBody}";
            }

            return responseBody;
        }

        return $"ERROR: All {_config.MaxRetries} attempts failed (503/429/network errors).";
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

        // 1. x-ratelimit-reset header (unix timestamp) - most precise
        if (response.Headers.TryGetValues("x-ratelimit-reset", out var resetValues))
        {
            if (long.TryParse(resetValues.FirstOrDefault(), out long resetUnix))
            {
                nextCall = DateTimeOffset.FromUnixTimeSeconds(resetUnix).AddMilliseconds(500);
            }
        }

        // 2. retry-after header (seconds) - override if present
        if (response.Headers.TryGetValues("retry-after", out var retryAfterValues))
        {
            if (int.TryParse(retryAfterValues.FirstOrDefault(), out int retryAfterSec))
            {
                var fromHeader = now.AddSeconds(retryAfterSec).AddMilliseconds(500);
                nextCall = nextCall.HasValue
                    ? (fromHeader > nextCall.Value ? fromHeader : nextCall.Value)
                    : fromHeader;
            }
        }

        // 3. retry_after from JSON body (fallback for 429)
        if (!nextCall.HasValue)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("retry_after", out var retryProp) &&
                    retryProp.TryGetInt32(out int retryAfterBody))
                {
                    nextCall = now.AddSeconds(retryAfterBody).AddMilliseconds(500);
                }
            }
            catch { }
        }

        // 4. Fallback: if 200 and no rate limit info, use fallback delay
        if (!nextCall.HasValue && response.IsSuccessStatusCode)
        {
            nextCall = now.AddMilliseconds(FallbackDelayMs);
        }

        if (nextCall.HasValue)
        {
            _nextAllowedCall = nextCall.Value;
            var waitMs = (int)(nextCall.Value - now).TotalMilliseconds;
            ConsoleUI.PrintInfo($"Next call allowed in {waitMs}ms (at {nextCall.Value:HH:mm:ss})");
        }
    }

    private async Task DelayBeforeRetry(int attempt)
    {
        var delay = _config.RetryDelayMs * (int)Math.Pow(2, attempt - 1);
        delay = Math.Min(delay, 30_000);

        // Use rate limit delay if it's longer than exponential backoff
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
