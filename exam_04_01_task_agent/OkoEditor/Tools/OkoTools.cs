using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OkoEditor.Config;
using OkoEditor.Services;
using OkoEditor.UI;

namespace OkoEditor.Tools;

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
        [Description("Nazwa akcji do wywołania, np. help, getIncidents, updateIncident, addIncident, getTasks, updateTask, done")] string action,
        [Description("Opcjonalne dodatkowe pola w formacie JSON object string, np. {\"id\": 123, \"classification\": \"animals\"}. Pomiń lub podaj null jeśli brak dodatkowych pól.")] string? additionalFieldsJson = null)
    {
        ConsoleUI.PrintStep($"CallVerifyApi: action={action}");

        object answer;

        // Treat the string literal "null" the same as actual null
        if (additionalFieldsJson?.Trim() == "null")
            additionalFieldsJson = null;

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

    [Description("Pobierz stronę z panelu webowego OKO (tylko do odczytu, bez modyfikacji). Używaj do rozpoznania struktury danych: ID incydentów, ID zadań, nazw pól.")]
    public async Task<string> FetchOkoPage(
        [Description("Pełny URL strony do pobrania, np. https://<oko_url>/ lub https://<oko_url>/incidents")] string url)
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

    /// <summary>
    /// Removes &lt;style&gt;, &lt;script&gt;, and inline style/script content from HTML
    /// so the LLM sees actual data rather than CSS/JS noise.
    /// </summary>
    private static string StripStylesAndScripts(string html)
    {
        html = Regex.Replace(html, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        // Collapse excessive blank lines left after stripping
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

        // Login form: action="/", fields: action=login, login, password, access_key
        var loginData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("action", "login"),
            new KeyValuePair<string, string>("login", _okoConfig.Username),
            new KeyValuePair<string, string>("password", _okoConfig.Password),
            new KeyValuePair<string, string>("access_key", _okoConfig.AccessKey)
        });

        var loginUrl = _okoConfig.BaseUrl + "/";
        var response = await client.PostAsync(loginUrl, loginData);
        var body = await response.Content.ReadAsStringAsync();
        ConsoleUI.PrintInfo($"OKO login POST {(int)response.StatusCode}, body length: {body.Length}");

        if (body.Contains("login-form") || body.Contains("Logowanie"))
            ConsoleUI.PrintError("OKO login failed — still showing login form. Check Oko__AccessKey in .env");
        else
            ConsoleUI.PrintInfo("OKO login successful");
    }
}
