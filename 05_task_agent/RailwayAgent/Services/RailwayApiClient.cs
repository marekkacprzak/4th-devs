using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RailwayAgent.Config;

namespace RailwayAgent.Services;

public class RailwayApiClient
{
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

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [API] Request (attempt {attempt}/{_config.MaxRetries}): {json}");
            Console.ResetColor();

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage response;

            try
            {
                response = await _http.PostAsync(_config.ApiUrl, content);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [API] Network error: {ex.Message}");
                Console.ResetColor();
                await DelayBeforeRetry(attempt);
                continue;
            }

            var responseBody = await response.Content.ReadAsStringAsync();

            // Update rate limit state from headers and body
            UpdateRateLimitState(response, responseBody);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [API] Response ({(int)response.StatusCode}): {responseBody}");
            Console.ResetColor();

            // Auto-retry on 503 (simulated outage)
            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  [API] 503 Service Unavailable - retrying...");
                Console.ResetColor();
                await DelayBeforeRetry(attempt);
                continue;
            }

            // Auto-retry on 429 (rate limit) - don't return error to agent
            if (response.StatusCode == (System.Net.HttpStatusCode)429)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  [API] 429 Rate limited - will auto-retry after waiting...");
                Console.ResetColor();
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
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [API] Rate limit: waiting {waitMs}ms until next allowed call...");
            Console.ResetColor();
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
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [API] Next call allowed in {waitMs}ms (at {nextCall.Value:HH:mm:ss})");
            Console.ResetColor();
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

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  [API] Waiting {delay}ms before retry...");
        Console.ResetColor();
        await Task.Delay(delay);
    }
}
