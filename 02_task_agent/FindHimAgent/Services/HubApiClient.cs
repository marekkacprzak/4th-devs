using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FindHimAgent.Config;
using FindHimAgent.UI;

namespace FindHimAgent.Services;

public class HubApiClient
{
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

    public async Task<string> PostJsonAsync(string url, object body)
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
                response = await _http.PostAsync(url, content);
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
                ConsoleUI.PrintRetry("429 Rate limited - will auto-retry after waiting...");
                continue;
            }

            if (!response.IsSuccessStatusCode)
                return $"HTTP {(int)response.StatusCode}: {responseBody}";

            return responseBody;
        }

        return $"ERROR: All {_config.MaxRetries} attempts failed.";
    }

    public async Task<string> GetJsonAsync(string url)
    {
        for (int attempt = 1; attempt <= _config.MaxRetries; attempt++)
        {
            await WaitForRateLimit();
            ConsoleUI.PrintApiRequest(attempt, _config.MaxRetries, $"GET {url}");

            HttpResponseMessage response;

            try
            {
                response = await _http.GetAsync(url);
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

    public async Task<string> GetPersonLocationsAsync(string name, string surname)
    {
        return await PostJsonAsync("https://hub.REDACTED.org/api/location", new
        {
            apikey = _config.ApiKey,
            name,
            surname
        });
    }

    public async Task<string> GetAccessLevelAsync(string name, string surname, int birthYear)
    {
        return await PostJsonAsync("https://hub.REDACTED.org/api/accesslevel", new
        {
            apikey = _config.ApiKey,
            name,
            surname,
            birthYear
        });
    }

    public async Task<string> GetPowerPlantsAsync()
    {
        var url = $"{_config.DataBaseUrl}/{_config.ApiKey}/findhim_locations.json";
        return await GetJsonAsync(url);
    }

    public async Task<string> SubmitAnswerAsync(object answer)
    {
        return await PostJsonAsync(_config.ApiUrl, new
        {
            apikey = _config.ApiKey,
            task = _config.TaskName,
            answer
        });
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
