using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using OkoEditor2.Config;
using OkoEditor2.Tools;
using OkoEditor2.UI;

namespace OkoEditor2.Services;

/// <summary>
/// Multi-phase orchestrator that solves the OKO task autonomously.
/// Phase 1: LLM fetches pages → C# extracts IDs/structure from HTML (no hallucination)
/// Phase 2: LLM analyzes structured data, outputs change plan
/// Phase 3: C# parses plan and executes API calls directly (no LLM delay — timing constraint)
/// </summary>
public class PhasedOrchestrator
{
    private readonly IChatClient _chatClient;
    private readonly IList<AITool> _allTools;     // FetchOkoPage + CallVerifyApi
    private readonly IList<AITool> _apiOnlyTools; // CallVerifyApi only (unused now but kept for fallback)
    private readonly RunLogger _logger;
    private readonly OkoTools _okoTools;
    private readonly string _phase1SystemPrompt;

    // Tracks page content returned by FetchOkoPage during Phase 1
    private readonly List<(string Url, string Content)> _fetchedPages = new();
    // Tracks the /verify help response
    private string _helpResponse = "";

    public PhasedOrchestrator(
        IChatClient chatClient,
        IList<AITool> allTools,
        IList<AITool> apiOnlyTools,
        RunLogger logger,
        OkoTools okoTools,
        OkoConfig okoConfig)
    {
        _chatClient = chatClient;
        _allTools = allTools;
        _apiOnlyTools = apiOnlyTools;
        _logger = logger;
        _okoTools = okoTools;
        _phase1SystemPrompt = Phase1SystemPromptTemplate.Replace("<oko_url>", okoConfig.BaseUrl);
    }

    public async Task<string> RunAsync()
    {
        // ── PHASE 1: Discovery ──────────────────────────────────────────────────
        ConsoleUI.PrintPhase(1, "Discovery — zbieranie danych z OKO");
        _logger.LogPhase(1, "Discovery");

        // Run tool-calling loop until we have all needed pages
        await RunDiscoveryToolLoop();

        // Build the structured report from fetched pages using C# regex
        // (avoids LLM hallucinating IDs when writing the report)
        var discoveryReport = BuildStructuredReport();
        _logger.LogInfo($"Phase 1 complete. Extracted report:\n{discoveryReport}");
        ConsoleUI.PrintStep("Phase 1 report extracted (C# parsed)");
        ConsoleUI.PrintInfo(discoveryReport);

        // ── PHASE 2: Analysis ───────────────────────────────────────────────────
        ConsoleUI.PrintPhase(2, "Analysis — analiza danych i plan zmian");
        _logger.LogPhase(2, "Analysis");

        var changePlan = await RunPhase(
            BuildPhase2Prompt(discoveryReport),
            "Przeanalizuj dane i przygotuj plan zmian w formacie === PLAN ===.",
            new List<AITool>(), // No tools — pure reasoning
            maxIterations: 2);

        _logger.LogInfo($"Phase 2 complete. Plan:\n{changePlan}");

        // ── PHASE 3: Execution ──────────────────────────────────────────────────
        // Direct C# execution — no LLM delays to stay within the API time window
        ConsoleUI.PrintPhase(3, "Execution — wykonanie zmian (bezpośrednio)");
        _logger.LogPhase(3, "Execution");

        var result = await ExecuteChangePlanDirectly(changePlan);

        _logger.LogInfo($"Phase 3 complete. Result: {result}");
        return result;
    }

    // ── Phase 1: Discovery Tool Loop ───────────────────────────────────────────

    /// <summary>
    /// Runs the LLM in tool-calling mode until it has fetched all required pages.
    /// Stops when: the model stops calling tools OR max 10 iterations reached.
    /// </summary>
    private async Task RunDiscoveryToolLoop()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, _phase1SystemPrompt),
            new(ChatRole.User, "Zbierz dane z systemu OKO. Wykonaj kroki 1-6.")
        };

        var toolOptions = new ChatOptions { Tools = _allTools };
        const int maxIterations = 15;

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            ConsoleUI.PrintLlmRequest(iteration + 1, maxIterations, messages.Count);
            _logger.LogLlmRequest(iteration + 1, maxIterations, messages.Count);

            ChatResponse response;
            try
            {
                response = await _chatClient.GetResponseAsync(messages, toolOptions);
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"LLM error: {ex.Message}");
                _logger.LogError("LLM", ex.ToString());
                return;
            }

            var toolCalls = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            var rawText = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<TextContent>()
                .Select(t => t.Text)
                .LastOrDefault();

            if (!string.IsNullOrWhiteSpace(rawText))
            {
                ConsoleUI.PrintLlmResponse(rawText);
                _logger.LogLlmResponse(rawText);
            }

            // No tool calls = model is done fetching
            if (toolCalls.Count == 0)
                return;

            // Check if we already have all needed pages
            bool hasIncidentList = _fetchedPages.Any(p => p.Url.EndsWith("ag3nts.org/") || p.Url.Contains("/incydenty") && !p.Url.Contains("/incydenty/"));
            bool hasTasks = _fetchedPages.Any(p => p.Url.Contains("/zadania") && !p.Url.Contains("/zadania/"));
            bool hasNotes = _fetchedPages.Any(p => p.Url.Contains("/notatki") && !p.Url.Contains("/notatki/"));
            bool hasNoteDetail = _fetchedPages.Any(p => p.Url.Contains("/notatki/"));
            bool hasHelp = !string.IsNullOrEmpty(_helpResponse);
            int incidentDetailCount = _fetchedPages.Count(p => p.Url.Contains("/incydenty/"));

            if (hasIncidentList && hasTasks && hasNotes && hasNoteDetail && hasHelp && incidentDetailCount >= 2)
            {
                _logger.LogInfo($"Required pages fetched ({incidentDetailCount} incident details). Stopping discovery loop.");
                return;
            }

            // Add assistant message
            var assistantMsg = new ChatMessage(ChatRole.Assistant,
                response.Messages.SelectMany(m => m.Contents).ToList());
            messages.Add(assistantMsg);

            // Execute each tool call and capture results
            foreach (var toolCall in toolCalls)
            {
                var argsJson = toolCall.Arguments != null
                    ? JsonSerializer.Serialize(toolCall.Arguments)
                    : null;

                ConsoleUI.PrintToolCallDetail(toolCall.Name, toolCall.CallId, argsJson);
                _logger.LogToolCall(toolCall.Name, toolCall.CallId, argsJson);

                var tool = _allTools.OfType<AIFunction>().FirstOrDefault(t => t.Name == toolCall.Name);
                string resultStr;
                bool isError = false;

                if (tool != null)
                {
                    try
                    {
                        var args = toolCall.Arguments != null
                            ? new AIFunctionArguments(toolCall.Arguments)
                            : null;
                        var result = await tool.InvokeAsync(args);
                        resultStr = result?.ToString() ?? "";

                        // Capture results for C# extraction
                        CaptureToolResult(toolCall.Name, argsJson, resultStr);
                    }
                    catch (Exception ex)
                    {
                        resultStr = $"Error: {ex.Message}";
                        isError = true;
                        ConsoleUI.PrintError($"Tool error: {ex.Message}");
                    }
                }
                else
                {
                    resultStr = $"Error: unknown tool '{toolCall.Name}'";
                    isError = true;
                }

                ConsoleUI.PrintToolResult(toolCall.CallId, resultStr, isError);
                _logger.LogToolResult(toolCall.CallId, resultStr, isError);

                var toolMsg = new ChatMessage(ChatRole.Tool,
                    [new FunctionResultContent(toolCall.CallId, resultStr)]);
                messages.Add(toolMsg);
            }
        }
    }

    /// <summary>
    /// Stores FetchOkoPage and CallVerifyApi results for later C# extraction.
    /// </summary>
    private void CaptureToolResult(string toolName, string? argsJson, string result)
    {
        if (toolName == "FetchOkoPage" && argsJson != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(argsJson);
                var url = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(url) && !_fetchedPages.Any(p => p.Url == url))
                    _fetchedPages.Add((url, result));
            }
            catch { }
        }
        else if (toolName == "CallVerifyApi" && argsJson != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(argsJson);
                var action = doc.RootElement.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
                if (action == "help")
                    _helpResponse = result;
            }
            catch { }
        }
    }

    // ── C# Data Extraction from HTML ───────────────────────────────────────────

    private record PageEntry(string Id, string Title, string Status = "");

    private string BuildStructuredReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== EXTRACTED DATA ===");

        // ── Help response ──
        if (!string.IsNullOrEmpty(_helpResponse))
        {
            sb.AppendLine("API HELP:");
            // Summarize key fields: actions and update format
            var updateSection = _helpResponse.Contains("update") ? "Actions: help, update, done. Update requires: page (incydenty|notatki|zadania), id (32-char hex), action; optional: content, title, done(YES/NO for zadania)." : _helpResponse[..Math.Min(500, _helpResponse.Length)];
            sb.AppendLine(updateSection);
            sb.AppendLine();
        }

        // ── Incidents ──
        var incidents = new List<PageEntry>();
        var incidentListPage = _fetchedPages.FirstOrDefault(p =>
            (p.Url.EndsWith(".org/") || p.Url.EndsWith(".org")) &&
            !p.Url.Contains("/incydenty/"));

        if (!string.IsNullOrEmpty(incidentListPage.Content))
        {
            incidents = ExtractLinksWithTitles(incidentListPage.Content, "incydenty");
            sb.AppendLine("INCYDENTY (lista):");
            foreach (var i in incidents)
                sb.AppendLine($"  ID: {i.Id} | Tytuł: {i.Title}");
            sb.AppendLine();
        }

        // ── Tasks ──
        var taskListPage = _fetchedPages.FirstOrDefault(p =>
            p.Url.Contains("/zadania") && !p.Url.Contains("/zadania/"));

        if (!string.IsNullOrEmpty(taskListPage.Content))
        {
            var tasks = ExtractLinksWithTitles(taskListPage.Content, "zadania");
            sb.AppendLine("ZADANIA (lista):");
            foreach (var t in tasks)
            {
                var status = taskListPage.Content.Contains($"/edit/{t.Id}") ? "niewykonane" : "?";
                // Check for done badge
                if (Regex.IsMatch(taskListPage.Content, $@"/zadania/{t.Id}[^>]*>[\s\S]{{0,500}}wykonane", RegexOptions.IgnoreCase))
                    status = "wykonane";
                sb.AppendLine($"  ID: {t.Id} | Tytuł: {t.Title} | Status: {status}");
            }
            sb.AppendLine();
        }

        // ── Notes ──
        var noteListPage = _fetchedPages.FirstOrDefault(p =>
            p.Url.Contains("/notatki") && !p.Url.Contains("/notatki/"));

        if (!string.IsNullOrEmpty(noteListPage.Content))
        {
            var notes = ExtractLinksWithTitles(noteListPage.Content, "notatki");
            sb.AppendLine("NOTATKI (lista):");
            foreach (var n in notes)
                sb.AppendLine($"  ID: {n.Id} | Tytuł: {n.Title}");
            sb.AppendLine();
        }

        // ── Skolwin incident detail ──
        var skolwinId = incidents.FirstOrDefault(i =>
            i.Title.Contains("Skolwin", StringComparison.OrdinalIgnoreCase))?.Id ?? "";

        if (!string.IsNullOrEmpty(skolwinId))
        {
            sb.AppendLine($"SKOLWIN ID: {skolwinId}");
            var skolwinDetail = _fetchedPages.FirstOrDefault(p =>
                p.Url.Contains($"/incydenty/{skolwinId}"));
            if (!string.IsNullOrEmpty(skolwinDetail.Content))
            {
                var title = ExtractHeroTitle(skolwinDetail.Content);
                var content = ExtractDetailContent(skolwinDetail.Content);
                sb.AppendLine($"SKOLWIN INCYDENT TYTUŁ: {title}");
                sb.AppendLine($"SKOLWIN INCYDENT TREŚĆ: {content[..Math.Min(400, content.Length)]}");
            }
            sb.AppendLine();
        }

        // ── Classification coding system (from notatki detail) ──
        var codingPage = _fetchedPages.FirstOrDefault(p =>
            p.Url.Contains("/notatki/") || p.Url.Contains("/incydenty/"));
        // Prefer a notatki detail page
        var notatiePage = _fetchedPages.FirstOrDefault(p => p.Url.Contains("/notatki/"));
        if (!string.IsNullOrEmpty(notatiePage.Content))
        {
            var codingText = ExtractDetailContent(notatiePage.Content);
            sb.AppendLine("KODY KLASYFIKACJI (z notatki):");
            sb.AppendLine(codingText[..Math.Min(800, codingText.Length)]);
            sb.AppendLine();
        }

        // ── Komarowo slot — search detail pages for "Komarowo" mention ──
        if (incidents.Count > 1 && !string.IsNullOrEmpty(skolwinId))
        {
            // First: look for an incident detail page containing "Komarowo"
            string? komarowoId = null;
            string? komarowoTitle = null;
            foreach (var inc in incidents.Where(i => i.Id != skolwinId))
            {
                var detailPage = _fetchedPages.FirstOrDefault(p =>
                    p.Url.Contains($"/incydenty/{inc.Id}"));
                if (!string.IsNullOrEmpty(detailPage.Content) &&
                    detailPage.Content.Contains("Komarowo", StringComparison.OrdinalIgnoreCase))
                {
                    komarowoId = inc.Id;
                    komarowoTitle = inc.Title;
                    break;
                }
            }
            // Fallback: last non-Skolwin incident
            if (komarowoId == null)
            {
                var last = incidents.LastOrDefault(i => i.Id != skolwinId);
                komarowoId = last?.Id;
                komarowoTitle = last?.Title;
            }
            if (!string.IsNullOrEmpty(komarowoId))
                sb.AppendLine($"SUGEROWANY SLOT DLA KOMAROWO: ID={komarowoId} (obecny tytuł: {komarowoTitle})");
        }

        sb.AppendLine("=== KONIEC DANYCH ===");
        return sb.ToString();
    }

    /// <summary>Extracts id+title pairs from links like href="/section/hexid".</summary>
    private static List<PageEntry> ExtractLinksWithTitles(string html, string section)
    {
        var result = new List<PageEntry>();
        var seen = new HashSet<string>();

        // Find all links with the section ID pattern and nearby title text
        var matches = Regex.Matches(html, $@"href=""/{section}/([a-f0-9]{{32}})""[\s\S]{{0,300}}?(?:<strong>|<h\d[^>]*>)(.*?)(?:</strong>|</h\d>)", RegexOptions.IgnoreCase);

        foreach (Match m in matches)
        {
            var id = m.Groups[1].Value;
            var rawTitle = m.Groups[2].Value;
            rawTitle = Regex.Replace(rawTitle, @"<[^>]+>", "").Trim();
            if (!seen.Contains(id) && !string.IsNullOrEmpty(rawTitle))
            {
                result.Add(new PageEntry(id, rawTitle));
                seen.Add(id);
            }
        }

        // Fallback: just find IDs if title extraction failed
        if (result.Count == 0)
        {
            var idMatches = Regex.Matches(html, $@"href=""/{section}/([a-f0-9]{{32}})""");
            foreach (Match m in idMatches)
            {
                var id = m.Groups[1].Value;
                if (!seen.Contains(id))
                {
                    result.Add(new PageEntry(id, "(title not found)"));
                    seen.Add(id);
                }
            }
        }

        return result;
    }

    private static string ExtractHeroTitle(string html)
    {
        var m = Regex.Match(html, @"hero-title[^>]*>(.*?)</h", RegexOptions.IgnoreCase);
        return m.Success ? Regex.Replace(m.Groups[1].Value, @"<[^>]+>", "").Trim() : "";
    }

    private static string ExtractDetailContent(string html)
    {
        var m = Regex.Match(html, @"detail-content[^>]*>([\s\S]*?)</p>", RegexOptions.IgnoreCase);
        if (m.Success)
            return Regex.Replace(m.Groups[1].Value, @"<[^>]+>", " ").Trim();

        // Fallback: strip all tags
        var stripped = Regex.Replace(html, @"<[^>]+>", " ");
        stripped = Regex.Replace(stripped, @"\s+", " ").Trim();
        return stripped[..Math.Min(600, stripped.Length)];
    }

    // ── Phase 3: Direct execution (no LLM) ────────────────────────────────────

    private record UpdateCommand(string Page, string Id, string? Title, string Content, bool Done = false);

    private static List<UpdateCommand> ParseChangePlan(string changePlan)
    {
        var commands = new List<UpdateCommand>();
        foreach (var line in changePlan.Split('\n'))
        {
            if (!line.TrimStart().StartsWith("UPDATE_", StringComparison.OrdinalIgnoreCase))
                continue;

            var pageMatch = Regex.Match(line, @"page=(\S+)");
            var idMatch = Regex.Match(line, @"id=([a-f0-9]{32})");
            var titleMatch = Regex.Match(line, @"title=""([^""]*)""");
            var contentMatch = Regex.Match(line, @"content=""([^""]*)""");
            bool done = line.Contains("done=YES", StringComparison.OrdinalIgnoreCase);

            if (!pageMatch.Success || !idMatch.Success || !contentMatch.Success)
                continue;

            commands.Add(new UpdateCommand(
                Page: pageMatch.Groups[1].Value.Trim(),
                Id: idMatch.Groups[1].Value,
                Title: titleMatch.Success ? titleMatch.Groups[1].Value : null,
                Content: contentMatch.Groups[1].Value,
                Done: done));
        }
        return commands;
    }

    private async Task<string> ExecuteChangePlanDirectly(string changePlan)
    {
        var commands = ParseChangePlan(changePlan);
        if (commands.Count == 0)
        {
            ConsoleUI.PrintError("Could not parse any UPDATE commands from Phase 2 plan.");
            _logger.LogError("Phase3", $"No commands parsed from plan:\n{changePlan}");
            return "ERROR: Could not parse change plan.";
        }

        ConsoleUI.PrintInfo($"Parsed {commands.Count} update command(s). Executing in rapid succession...");
        _logger.LogInfo($"Phase 3: Executing {commands.Count} commands directly.");

        // Execute all 3 updates immediately
        foreach (var cmd in commands)
        {
            var fields = new Dictionary<string, object?>();
            fields["page"] = cmd.Page;
            fields["id"] = cmd.Id;
            if (cmd.Title != null) fields["title"] = cmd.Title;
            fields["content"] = cmd.Content;
            if (cmd.Done) fields["done"] = "YES";
            var json = JsonSerializer.Serialize(fields);

            ConsoleUI.PrintStep($"Direct update: page={cmd.Page} id={cmd.Id}");
            var updateResult = await _okoTools.CallVerifyApi("update", json);
            ConsoleUI.PrintInfo($"Update result: {updateResult}");
            _logger.LogInfo($"Update result: {updateResult}");
        }

        // Call done immediately — no LLM processing delay
        ConsoleUI.PrintStep("Calling done...");
        var doneResult = await _okoTools.CallVerifyApi("done", null);
        ConsoleUI.PrintInfo($"Done result: {doneResult}");
        _logger.LogInfo($"Done result: {doneResult}");

        return doneResult;
    }

    // ── Phase runner ──────────────────────────────────────────────────────────

    private async Task<string> RunPhase(
        string systemPrompt,
        string userGoal,
        IList<AITool> tools,
        int maxIterations)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userGoal)
        };

        var options = new ChatOptions { Tools = tools };

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            ConsoleUI.PrintLlmRequest(iteration + 1, maxIterations, messages.Count);
            _logger.LogLlmRequest(iteration + 1, maxIterations, messages.Count);

            ChatResponse response;
            try
            {
                response = await _chatClient.GetResponseAsync(messages, options);
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"LLM error: {ex.Message}");
                _logger.LogError("LLM", ex.ToString());
                return $"ERROR: LLM call failed: {ex.Message}";
            }

            var toolCalls = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            var rawText = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<TextContent>()
                .Select(t => t.Text)
                .LastOrDefault();

            if (!string.IsNullOrWhiteSpace(rawText))
            {
                ConsoleUI.PrintLlmResponse(rawText);
                _logger.LogLlmResponse(rawText);
            }

            if (toolCalls.Count == 0)
                return StripThinkingTokens(rawText ?? "");

            var assistantMsg = new ChatMessage(ChatRole.Assistant,
                response.Messages.SelectMany(m => m.Contents).ToList());
            messages.Add(assistantMsg);

            foreach (var toolCall in toolCalls)
            {
                var argsJson = toolCall.Arguments != null
                    ? JsonSerializer.Serialize(toolCall.Arguments)
                    : null;

                ConsoleUI.PrintToolCallDetail(toolCall.Name, toolCall.CallId, argsJson);
                _logger.LogToolCall(toolCall.Name, toolCall.CallId, argsJson);

                var tool = tools.OfType<AIFunction>().FirstOrDefault(t => t.Name == toolCall.Name);
                string resultStr;
                bool isError = false;

                if (tool != null)
                {
                    try
                    {
                        var args = toolCall.Arguments != null
                            ? new AIFunctionArguments(toolCall.Arguments)
                            : null;
                        var result = await tool.InvokeAsync(args);
                        resultStr = result?.ToString() ?? "";
                    }
                    catch (Exception ex)
                    {
                        resultStr = $"Error: {ex.Message}";
                        isError = true;
                        ConsoleUI.PrintError($"Tool error: {ex.Message}");
                    }
                }
                else
                {
                    resultStr = $"Error: unknown tool '{toolCall.Name}'";
                    isError = true;
                }

                ConsoleUI.PrintToolResult(toolCall.CallId, resultStr, isError);
                _logger.LogToolResult(toolCall.CallId, resultStr, isError);

                var toolMsg = new ChatMessage(ChatRole.Tool,
                    [new FunctionResultContent(toolCall.CallId, resultStr)]);
                messages.Add(toolMsg);
            }
        }

        return "ERROR: Maximum iterations reached for this phase.";
    }

    // ── Phase prompts ──────────────────────────────────────────────────────────

    private const string Phase1SystemPromptTemplate = """
        Jesteś agentem rozpoznawczym systemu OKO. Pobierz dane z następujących źródeł:

        KROK 1: CallVerifyApi("help", null) — poznaj dostępne akcje API
        KROK 2: FetchOkoPage("https://<oko_url>/") — lista incydentów (zapamiętaj WSZYSTKIE ID)
        KROK 3: FetchOkoPage("https://<oko_url>/notatki") — lista notatek
        KROK 4: FetchOkoPage("https://<oko_url>/zadania") — lista zadań
        KROK 5: FetchOkoPage stron szczegółów KAŻDEGO incydentu z listy — https://<oko_url>/incydenty/<ID> dla każdego ID z kroku 2
        KROK 6: FetchOkoPage strony szczegółów pierwszej notatki z listy z kroku 3

        ZASADY:
        - Każdą stronę pobieraj TYLKO RAZ
        - NIE wywołuj update ani done
        - URLs po polsku: /incydenty, /notatki, /zadania
        - Po wykonaniu WSZYSTKICH kroków (kroki 1-6 w tym szczegóły każdego incydentu) możesz zakończyć
        """;

    private static string BuildPhase2Prompt(string discoveryReport) => $"""
        Jesteś analitykiem systemu OKO. Oto wyekstrahowane dane:

        {discoveryReport}

        Zadanie:
        1. Zmienić klasyfikację incydentu Skolwin na zwierzęta (kod MOVE04 = zwierzęta)
        2. Oznaczyć zadanie Skolwin jako wykonane z treścią o bobrach
        3. Dodać incydent o ruchu ludzi w Komarowie (MOVE01 = człowiek) używając SUGEROWANEGO SLOTU DLA KOMAROWO

        WAŻNE: Użyj DOKŁADNIE tych ID które są podane w danych powyżej.
        Treść (content) musi być CZYSTYM TEKSTEM — zero tagów HTML.
        Tytuł incydentu MUSI zaczynać się od kodu MOVE04 lub MOVE01.

        Odpowiedz WYŁĄCZNIE w formacie:

        === PLAN ===
        UPDATE_1: page=incydenty id=<dokładny_32hex_z_danych> title="MOVE04 Ruch zwierząt nieopodal Skolwina" content="Zidentyfikowano bobry w okolicach Skolwina."
        UPDATE_2: page=zadania id=<dokładny_32hex_z_danych> done=YES content="Widziano bobry w okolicach Skolwina. Zadanie zakończone."
        UPDATE_3: page=incydenty id=<dokładny_32hex_sugerowanego_slotu> title="MOVE01 Wykryto ruch ludzi w okolicach niezamieszkanego miasta Komarowo" content="Czujniki zarejestrowały ruch ludzi w pobliżu opuszczonego miasta Komarowo."
        === KONIEC PLANU ===
        """;

    private static string StripThinkingTokens(string text)
        => Regex.Replace(text, @"<think>[\s\S]*?</think>", "").Trim();
}
