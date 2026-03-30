using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OkoEditor2.Config;
using OkoEditor2.Services;
using OkoEditor2.UI;

namespace OkoEditor2.Tools;

public class OkoTools
{
    private readonly CentralaApiClient _centrala;
    private readonly OkoConfig _okoConfig;
    private readonly HttpClient _httpClient;
    private CookieContainer? _cookieContainer;

    public OkoTools(CentralaApiClient centrala, OkoConfig okoConfig, HttpClient httpClient)
    {
        _centrala = centrala;
        _okoConfig = okoConfig;
        _httpClient = httpClient;
    }

    [Description("Wywołaj API /verify w centrali. Podaj nazwę akcji i opcjonalne dodatkowe pola jako JSON string.")]
    public async Task<string> CallVerifyApi(
        [Description("Nazwa akcji do wywołania: help, update, done")] string action,
        [Description("Opcjonalne dodatkowe pola w formacie JSON object string, np. {\"page\":\"incydenty\",\"id\":\"abc\",\"action\":\"update\",\"title\":\"...\",\"content\":\"...\"}. Pomiń lub podaj null jeśli brak dodatkowych pól.")] string? additionalFieldsJson = null)
    {
        ConsoleUI.PrintStep($"CallVerifyApi: action={action}");

        // Treat the string literal "null" the same as actual null
        if (additionalFieldsJson?.Trim() == "null")
            additionalFieldsJson = null;

        object answer;

        if (!string.IsNullOrWhiteSpace(additionalFieldsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(additionalFieldsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    answer = new { action };
                    return await _centrala.VerifyAsync(answer);
                }

                var dict = new Dictionary<string, object?>();
                dict["action"] = action;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => (object?)prop.Value.GetString(),
                        JsonValueKind.Number when prop.Value.TryGetInt32(out var i) => i,
                        JsonValueKind.Number => prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => prop.Value.GetRawText()
                    };
                }
                answer = dict;
            }
            catch (JsonException ex)
            {
                ConsoleUI.PrintError($"Failed to parse additionalFieldsJson: {ex.Message}. Calling with action only.");
                answer = new { action };
            }
        }
        else
        {
            answer = new { action };
        }

        return await _centrala.VerifyAsync(answer);
    }

    [Description("Pobierz stronę z panelu webowego OKO (tylko do odczytu). Używaj do rozpoznania struktury danych: ID incydentów, ID zadań, treści notatek z kodami klasyfikacji.")]
    public async Task<string> FetchOkoPage(
        [Description("Pełny URL strony do pobrania. Używaj polskich URL-i: https://<oko_url>/ (incydenty), https://<oko_url>/zadania, https://<oko_url>/notatki, https://<oko_url>/incydenty/<id>, https://<oko_url>/notatki/<id>")] string url)
    {
        ConsoleUI.PrintStep($"FetchOkoPage: {url}");

        try
        {
            if (_cookieContainer == null)
                await LoginAsync();

            var handler = new HttpClientHandler { CookieContainer = _cookieContainer! };
            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(30);

            var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            body = StripStylesAndScripts(body);

            // Truncate to stay within LLM context limits
            const int maxLen = 8000;
            if (body.Length > maxLen)
                body = body[..maxLen] + "\n...[truncated]";

            return body;
        }
        catch (Exception ex)
        {
            return $"Error fetching page: {ex.Message}";
        }
    }

    private static string StripStylesAndScripts(string html)
    {
        html = Regex.Replace(html, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"\n{3,}", "\n\n");
        return html.Trim();
    }

    private async Task LoginAsync()
    {
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            AllowAutoRedirect = true
        };
        using var client = new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(30);

        var loginData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("action", "login"),
            new KeyValuePair<string, string>("login", _okoConfig.Username),
            new KeyValuePair<string, string>("password", _okoConfig.Password),
            new KeyValuePair<string, string>("access_key", _okoConfig.AccessKey)
        });

        var baseUrl = _okoConfig.BaseUrl.TrimEnd('/');
        var loginUrl = baseUrl + "/";
        var response = await client.PostAsync(loginUrl, loginData);
        var body = await response.Content.ReadAsStringAsync();
        ConsoleUI.PrintInfo($"OKO login POST {(int)response.StatusCode}, body length: {body.Length}");

        // Check for password input — present only on the login form, not on authenticated pages
        if (body.Contains("type=\"password\"") || body.Contains("type='password'"))
            ConsoleUI.PrintError("OKO login failed — still showing login form. Check Oko__AccessKey in .env");
        else
            ConsoleUI.PrintInfo("OKO login successful");
    }
}
