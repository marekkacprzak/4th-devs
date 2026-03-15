using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RailwayAgent.Config;

namespace RailwayAgent.Services;

public class RailwayApiClient
{
    private readonly HttpClient _http;
    private readonly RailwayConfig _config;

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

            // Check rate limit headers
            await RespectRateLimit(response);

            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  [API] 503 Service Unavailable - retrying...");
                Console.ResetColor();
                await DelayBeforeRetry(attempt);
                continue;
            }

            var responseBody = await response.Content.ReadAsStringAsync();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [API] Response ({(int)response.StatusCode}): {responseBody}");
            Console.ResetColor();

            if (!response.IsSuccessStatusCode)
            {
                return $"HTTP {(int)response.StatusCode}: {responseBody}";
            }

            return responseBody;
        }

        return $"ERROR: All {_config.MaxRetries} attempts failed (503 or network errors).";
    }

    private async Task RespectRateLimit(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues))
        {
            if (int.TryParse(remainingValues.FirstOrDefault(), out int remaining) && remaining <= 1)
            {
                int resetDelay = _config.RetryDelayMs;

                if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues))
                {
                    if (long.TryParse(resetValues.FirstOrDefault(), out long resetUnix))
                    {
                        var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetUnix);
                        var waitMs = (int)(resetTime - DateTimeOffset.UtcNow).TotalMilliseconds;
                        if (waitMs > 0)
                            resetDelay = Math.Min(waitMs + 500, 30_000);
                    }
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  [API] Rate limit near ({remaining} remaining). Waiting {resetDelay}ms...");
                Console.ResetColor();
                await Task.Delay(resetDelay);
            }
        }

        if (response.Headers.TryGetValues("Retry-After", out var retryAfterValues))
        {
            if (int.TryParse(retryAfterValues.FirstOrDefault(), out int retryAfterSec))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  [API] Retry-After: {retryAfterSec}s");
                Console.ResetColor();
                await Task.Delay(retryAfterSec * 1000);
            }
        }
    }

    private async Task DelayBeforeRetry(int attempt)
    {
        var delay = _config.RetryDelayMs * (int)Math.Pow(2, attempt - 1);
        delay = Math.Min(delay, 30_000);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  [API] Waiting {delay}ms before retry...");
        Console.ResetColor();
        await Task.Delay(delay);
    }
}
