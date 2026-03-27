using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SavethemAgent.Config;
using SavethemAgent.UI;

namespace SavethemAgent.Services;

public class HubApiClient
{
    private static readonly ActivitySource Activity = new("SavethemAgent.Hub");

    private readonly HttpClient _http;
    private readonly HubConfig _config;
    private readonly RequestLogger? _logger;

    private DateTimeOffset _nextAllowedCall = DateTimeOffset.MinValue;
    private const int FallbackDelayMs = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public HubApiClient(HttpClient http, HubConfig config, RequestLogger? logger = null)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    /// <summary>Search for available tools by natural-language query.</summary>
    public async Task<string> ToolSearchAsync(string query)
    {
        using var span = Activity.StartActivity("hub.tool_search");
        span?.SetTag("query", query);
        return await PostJsonAsync(_config.ToolSearchUrl, new
        {
            apikey = _config.ApiKey,
            query
        });
    }

    /// <summary>Call a discovered tool URL with a query. Resolves relative URLs against the ToolSearchUrl base.</summary>
    public async Task<string> CallToolAsync(string toolUrl, string query)
    {
        // Resolve relative URLs (e.g. "/api/maps") against the hub base URL
        if (!toolUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var baseUri = new Uri(_config.ToolSearchUrl);
            var path = toolUrl.StartsWith('/') ? toolUrl : "/" + toolUrl;
            toolUrl = $"{baseUri.Scheme}://{baseUri.Host}{(baseUri.IsDefaultPort ? "" : $":{baseUri.Port}")}{path}";
        }

        using var span = Activity.StartActivity("hub.call_tool");
        span?.SetTag("tool_url", toolUrl);
        span?.SetTag("query", query);
        return await PostJsonAsync(toolUrl, new
        {
            apikey = _config.ApiKey,
            query
        });
    }

    /// <summary>Submit the final route answer to the verify endpoint.</summary>
    public async Task<string> VerifyAsync(string[] answer)
    {
        using var span = Activity.StartActivity("hub.verify");
        span?.SetTag("answer.length", answer.Length);
        return await PostJsonAsync(_config.VerifyUrl, new
        {
            apikey = _config.ApiKey,
            task = _config.TaskName,
            answer
        });
    }

    public async Task<string> PostJsonAsync(string url, object body)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);

        for (int attempt = 1; attempt <= _config.MaxRetries; attempt++)
        {
            await WaitForRateLimit();
            using var span = Activity.StartActivity("http.post");
            span?.SetTag("http.url", url);
            span?.SetTag("http.method", "POST");
            span?.SetTag("http.attempt", attempt);
            span?.SetTag("http.request.body", json);

            _logger?.LogRequest("POST", url, json);
            ConsoleUI.PrintApiRequest(attempt, _config.MaxRetries, json);

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage response;

            try
            {
                response = await _http.PostAsync(url, content);
            }
            catch (Exception ex)
            {
                span?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger?.LogResponse(0, $"NETWORK ERROR: {ex.Message}");
                ConsoleUI.PrintError($"Network error: {ex.Message}");
                await DelayBeforeRetry(attempt);
                continue;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            span?.SetTag("http.status_code", (int)response.StatusCode);
            span?.SetTag("http.response.body", responseBody);

            _logger?.LogResponse((int)response.StatusCode, responseBody);
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
                return $"HTTP {(int)response.StatusCode}: {responseBody}";

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

        if (!nextCall.HasValue && !string.IsNullOrEmpty(responseBody))
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
