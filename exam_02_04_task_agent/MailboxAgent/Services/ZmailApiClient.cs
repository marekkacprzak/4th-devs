using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailboxAgent.Config;
using MailboxAgent.UI;

namespace MailboxAgent.Services;

public class ZmailApiClient
{
    private static readonly ActivitySource Activity = new("MailboxAgent.Zmail");

    private readonly HttpClient _http;
    private readonly HubConfig _config;

    private DateTimeOffset _nextAllowedCall = DateTimeOffset.MinValue;
    private const int FallbackDelayMs = 3000;
    private const int RateLimitFallbackDelayMs = 5000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ZmailApiClient(HttpClient http, HubConfig config)
    {
        _http = http;
        _config = config;
    }

    public async Task<string> CallHelpAsync()
    {
        using var span = Activity.StartActivity("zmail.help");
        var body = new Dictionary<string, object>
        {
            ["apikey"] = _config.ApiKey,
            ["action"] = "help"
        };
        return await PostAsync(body, span);
    }

    public async Task<string> GetInboxAsync(int page = 1, int perPage = 20)
    {
        using var span = Activity.StartActivity("zmail.get_inbox");
        span?.SetTag("zmail.page", page);
        var body = new Dictionary<string, object>
        {
            ["apikey"] = _config.ApiKey,
            ["action"] = "getInbox",
            ["page"] = page,
            ["perPage"] = perPage
        };
        return await PostAsync(body, span);
    }

    public async Task<string> SearchAsync(string query, int page = 1, int perPage = 20)
    {
        using var span = Activity.StartActivity("zmail.search");
        span?.SetTag("zmail.query", query);
        span?.SetTag("zmail.page", page);
        var body = new Dictionary<string, object>
        {
            ["apikey"] = _config.ApiKey,
            ["action"] = "search",
            ["query"] = query,
            ["page"] = page,
            ["perPage"] = perPage
        };
        return await PostAsync(body, span);
    }

    public async Task<string> GetThreadAsync(int threadId)
    {
        using var span = Activity.StartActivity("zmail.get_thread");
        span?.SetTag("zmail.thread_id", threadId);
        var body = new Dictionary<string, object>
        {
            ["apikey"] = _config.ApiKey,
            ["action"] = "getThread",
            ["threadID"] = threadId
        };
        return await PostAsync(body, span);
    }

    public async Task<string> GetMessagesAsync(object ids)
    {
        using var span = Activity.StartActivity("zmail.get_messages");
        span?.SetTag("zmail.ids", ids.ToString());
        var body = new Dictionary<string, object>
        {
            ["apikey"] = _config.ApiKey,
            ["action"] = "getMessages",
            ["ids"] = ids
        };
        return await PostAsync(body, span);
    }

    public async Task<string> ResetAsync()
    {
        using var span = Activity.StartActivity("zmail.reset");
        var body = new Dictionary<string, object>
        {
            ["apikey"] = _config.ApiKey,
            ["action"] = "reset"
        };
        return await PostAsync(body, span);
    }

    private async Task<string> PostAsync(Dictionary<string, object> body, System.Diagnostics.Activity? span)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);

        for (int attempt = 1; attempt <= _config.MaxRetries; attempt++)
        {
            await WaitForRateLimit();

            ConsoleUI.PrintApiRequest(attempt, _config.MaxRetries, json);

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage response;

            try
            {
                response = await _http.PostAsync(_config.ZmailUrl, content);
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"Network error: {ex.Message}");
                await DelayBeforeRetry(attempt);
                continue;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
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
                var now429 = DateTimeOffset.UtcNow;
                if (_nextAllowedCall <= now429)
                {
                    var backoff = RateLimitFallbackDelayMs * (int)Math.Pow(2, attempt - 1);
                    backoff = Math.Min(backoff, 60_000);
                    _nextAllowedCall = now429.AddMilliseconds(backoff);
                    ConsoleUI.PrintRetry($"429 Rate limited - waiting {backoff}ms before retry...");
                }
                else
                {
                    ConsoleUI.PrintRetry("429 Rate limited - will auto-retry after waiting...");
                }
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                span?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)response.StatusCode}");
                return $"HTTP {(int)response.StatusCode}: {responseBody}";
            }

            return responseBody;
        }

        span?.SetStatus(ActivityStatusCode.Error, "All attempts failed");
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

        if (response.Headers.TryGetValues("retry-after", out var retryAfterValues))
        {
            if (int.TryParse(retryAfterValues.FirstOrDefault(), out int retryAfterSec))
            {
                _nextAllowedCall = now.AddSeconds(retryAfterSec).AddMilliseconds(500);
                return;
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("retry_after", out var retryProp) &&
                retryProp.TryGetInt32(out int retryAfterBody))
            {
                _nextAllowedCall = now.AddSeconds(retryAfterBody).AddMilliseconds(500);
                return;
            }
        }
        catch { }

        if (response.IsSuccessStatusCode)
            _nextAllowedCall = now.AddMilliseconds(FallbackDelayMs);
    }

    private async Task DelayBeforeRetry(int attempt)
    {
        var delay = _config.RetryDelayMs * (int)Math.Pow(2, attempt - 1);
        delay = Math.Min(delay, 30_000);
        ConsoleUI.PrintInfo($"Waiting {delay}ms before retry...");
        await Task.Delay(delay);
    }
}
