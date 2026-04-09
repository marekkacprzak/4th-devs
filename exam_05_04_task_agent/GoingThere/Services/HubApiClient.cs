using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoingThere.Config;
using GoingThere.UI;

namespace GoingThere.Services;

public class HubApiClient
{
    private static readonly ActivitySource Activity = new("GoingThere.Hub");

    private readonly HttpClient _http;
    private readonly HubConfig _config;
    private readonly RunLogger _logger;

    private DateTimeOffset _nextAllowedCall = DateTimeOffset.MinValue;
    private DateTimeOffset _lastCallTime = DateTimeOffset.MinValue;
    private const int MinInterRequestDelayMs = 2000; // 2s between all calls is sufficient

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public HubApiClient(HttpClient http, HubConfig config, RunLogger logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    /// <summary>POST /verify with a game command (start, go, left, right).</summary>
    public async Task<string> SendCommandAsync(string command)
    {
        using var span = Activity.StartActivity("hub.command");
        span?.SetTag("game.command", command);
        var url = $"{_config.ApiUrl}/verify";
        ConsoleUI.PrintStep($"SendCommand: {command} → POST {url}");
        return await PostJsonAsync(url, new
        {
            apikey = _config.ApiKey,
            task = _config.TaskName,
            answer = new { command }
        });
    }

    /// <summary>GET /api/frequencyScanner — check if OKO radar is active.</summary>
    public async Task<string> CheckFrequencyScannerAsync()
    {
        using var span = Activity.StartActivity("hub.frequencyScanner.check");
        var url = $"{_config.ApiUrl}/api/frequencyScanner?key={Uri.EscapeDataString(_config.ApiKey)}";
        ConsoleUI.PrintStep($"CheckFrequencyScanner → GET {url}");
        return await GetAsync(url);
    }

    /// <summary>POST /api/frequencyScanner — disarm a detected radar trap.</summary>
    public async Task<string> DisarmRadarAsync(int frequency, string disarmHash)
    {
        using var span = Activity.StartActivity("hub.frequencyScanner.disarm");
        span?.SetTag("radar.frequency", frequency);
        var url = $"{_config.ApiUrl}/api/frequencyScanner";
        ConsoleUI.PrintStep($"DisarmRadar: freq={frequency} → POST {url}");
        return await PostJsonAsync(url, new
        {
            apikey = _config.ApiKey,
            frequency,
            disarmHash
        });
    }

    /// <summary>POST /api/getmessage — fetch radio hint about rock positions.
    /// Single attempt only — no retries, to avoid wasting rate-limit quota on 429 retries.</summary>
    public async Task<string> GetRadioHintAsync()
    {
        using var span = Activity.StartActivity("hub.getmessage");
        var url = $"{_config.ApiUrl}/api/getmessage";
        ConsoleUI.PrintStep($"GetRadioHint → POST {url}");
        return await PostJsonAsync(url, new { apikey = _config.ApiKey }, maxRetries: 1);
    }

    public async Task<string> GetAsync(string url)
    {
        for (int attempt = 1; attempt <= _config.MaxRetries; attempt++)
        {
            await WaitForRateLimit();
            using var span = Activity.StartActivity("http.get");
            span?.SetTag("http.url", url);
            span?.SetTag("http.attempt", attempt);

            ConsoleUI.PrintApiRequest(attempt, _config.MaxRetries, $"GET {url}");
            _logger.LogApiRequest(url, "GET");

            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync(url);
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

            if ((int)response.StatusCode >= 500)
            {
                ConsoleUI.PrintRetry($"{(int)response.StatusCode} Server Error - retrying...");
                await DelayBeforeRetry(attempt);
                continue;
            }

            if (response.StatusCode == (System.Net.HttpStatusCode)429)
            {
                var wait429 = Math.Min(2000 * (int)Math.Pow(2, attempt - 1), 30000);
                ConsoleUI.PrintRetry($"429 Rate limited - waiting {wait429}ms...");
                await Task.Delay(wait429);
                continue;
            }

            // Return body regardless of status — caller decides how to interpret
            return responseBody;
        }

        return $"ERROR: All {_config.MaxRetries} GET attempts failed for {url}";
    }

    public async Task<string> PostJsonAsync(string url, object body, int? maxRetries = null)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        int retryLimit = maxRetries ?? _config.MaxRetries;

        for (int attempt = 1; attempt <= retryLimit; attempt++)
        {
            await WaitForRateLimit();
            using var span = Activity.StartActivity("http.post");
            span?.SetTag("http.url", url);
            span?.SetTag("http.attempt", attempt);

            ConsoleUI.PrintApiRequest(attempt, _config.MaxRetries, json);
            _logger.LogApiRequest(url, json);

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

            if ((int)response.StatusCode >= 500)
            {
                ConsoleUI.PrintRetry($"{(int)response.StatusCode} Server Error - retrying...");
                await DelayBeforeRetry(attempt);
                continue;
            }

            if (response.StatusCode == (System.Net.HttpStatusCode)429)
            {
                var wait429 = Math.Min(2000 * (int)Math.Pow(2, attempt - 1), 30000);
                ConsoleUI.PrintRetry($"429 Rate limited - waiting {wait429}ms...");
                await Task.Delay(wait429);
                continue;
            }

            if (!response.IsSuccessStatusCode)
                return $"HTTP {(int)response.StatusCode}: {responseBody}";

            return responseBody;
        }

        return $"ERROR: All {retryLimit} attempts failed for {url}";
    }

    private async Task WaitForRateLimit()
    {
        // Enforce minimum delay between any two requests
        var now = DateTimeOffset.UtcNow;
        var sinceLastCall = (now - _lastCallTime).TotalMilliseconds;
        if (sinceLastCall < MinInterRequestDelayMs)
        {
            var minWait = (int)(MinInterRequestDelayMs - sinceLastCall);
            await Task.Delay(minWait);
        }

        // Also enforce rate-limit window from headers
        now = DateTimeOffset.UtcNow;
        if (_nextAllowedCall > now)
        {
            var waitMs = (int)(_nextAllowedCall - now).TotalMilliseconds;
            ConsoleUI.PrintRateLimit(waitMs);
            await Task.Delay(waitMs);
        }

        _lastCallTime = DateTimeOffset.UtcNow;
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
