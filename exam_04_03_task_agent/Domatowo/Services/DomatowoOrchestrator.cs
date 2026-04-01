using System.Text.Json;
using Domatowo.Models;
using Domatowo.Tools;
using Domatowo.UI;

namespace Domatowo.Services;

/// <summary>
/// C#-driven orchestrator for the Domatowo rescue mission.
///
/// Map layout (from getMap response, verified):
///   - Row 6 (A6-J6): main horizontal road — spawn zone
///   - Row 9 (B9-J9): second horizontal road
///   - Col D  (D1-D9): vertical road connecting both rows
///   - Col I  (I2-I6, I9): partial vertical road
///   - E2 is a road cell adjacent to block3 cluster F1/G1/F2/G2
///
/// Block3 cells (3-floor, tallest = survivor's hiding place):
///   North:  F1, G1, F2, G2
///   SW:     A10, B10, C10, A11, B11, C11
///   SE:     H10, I10, H11, I11
///
/// Strategy:
///   T1 (2 scouts) drops at E2 → scouts search F1/G1/F2/G2
///   T2 (4 scouts) drops at B9 (2 scouts) and I9 (2 scouts) → scouts search SW/SE clusters
/// </summary>
public class DomatowoOrchestrator
{
    private readonly DomatowoTools _tools;
    private readonly RunLogger _logger;
    private int _actionPointsLeft = 300;

    public DomatowoOrchestrator(DomatowoTools tools, RunLogger logger)
    {
        _tools = tools;
        _logger = logger;
    }

    public async Task<string> RunAsync()
    {
        _logger.LogPhase(0, "Discovery");
        ConsoleUI.PrintPhase(0, "Discovery");

        // Reset to get a clean state
        var resetResp = await _tools.Reset();
        _logger.LogInfo($"Reset: {resetResp}");
        UpdateBudget(resetResp);

        // Get action costs for reference
        var costsResp = await _tools.ActionCost();
        _logger.LogInfo($"ActionCost: {costsResp}");

        _logger.LogPhase(1, "Find Targets");
        ConsoleUI.PrintPhase(1, "Find Targets");

        // Use searchSymbol to directly find all block3 (tallest building) cells
        var b3Resp = await _tools.SearchSymbol("B3");
        _logger.LogInfo($"SearchSymbol B3: {b3Resp}");
        ConsoleUI.PrintInfo($"SearchSymbol B3: {Truncate(b3Resp, 300)}");

        var allTargets = ParseCoordList(b3Resp);
        ConsoleUI.PrintInfo($"Block3 cells found: {string.Join(", ", allTargets)}");

        if (allTargets.Count == 0)
        {
            _logger.LogError("RunAsync", "No block3 targets found. Check searchSymbol response.");
            // Fallback: hard-code known block3 positions from map analysis
            allTargets = new List<string> { "F1", "G1", "F2", "G2", "A10", "B10", "C10", "A11", "B11", "C11", "H10", "I10", "H11", "I11" };
            ConsoleUI.PrintInfo($"Using hardcoded block3 fallback: {string.Join(", ", allTargets)}");
        }

        // Split into clusters
        var northTargets = allTargets.Where(c => GetRow(c) <= 5).OrderBy(c => c).ToList();
        var swTargets = allTargets.Where(c => GetRow(c) > 5 && GetColIdx(c) <= 2).OrderBy(c => c).ToList();
        var seTargets = allTargets.Where(c => GetRow(c) > 5 && GetColIdx(c) >= 7).OrderBy(c => c).ToList();

        ConsoleUI.PrintInfo($"North: {string.Join(", ", northTargets)}");
        ConsoleUI.PrintInfo($"SW: {string.Join(", ", swTargets)}");
        ConsoleUI.PrintInfo($"SE: {string.Join(", ", seTargets)}");

        _logger.LogPhase(2, "Deployment & Search");
        ConsoleUI.PrintPhase(2, "Deployment & Search");

        string? survivorAt = null;

        // ── T1: North cluster (F1, G1, F2, G2) ──────────────────────────────
        // Drop-off: E2 (road, adjacent to F2). Route: A6→D6→D2→E2 = 8pt
        if (northTargets.Count > 0)
        {
            ConsoleUI.PrintStep($"T1: North cluster {string.Join(" ", northTargets)}");
            survivorAt = await SearchWithTransporterAsync(
                passengers: Math.Min(2, northTargets.Count),
                dropOff: "E2",
                targets: northTargets);
            if (survivorAt != null) goto Evacuate;
        }

        // ── T2: South clusters ────────────────────────────────────────────────
        // SW drop-off: B9 (road). Route: B6→D6→D9→B9 = 7pt
        // SE drop-off: I9 (road). Route: (from B9) B9→I9 = 7pt additional
        var southTargets = swTargets.Concat(seTargets).ToList();
        if (southTargets.Count > 0)
        {
            // Create T2 with scouts for SW
            if (swTargets.Count > 0)
            {
                ConsoleUI.PrintStep($"T2-SW: {string.Join(" ", swTargets)}");
                survivorAt = await SearchWithTransporterAsync(
                    passengers: Math.Min(3, swTargets.Count),
                    dropOff: "B9",
                    targets: swTargets);
                if (survivorAt != null) goto Evacuate;
            }

            // Create T3 (or another transporter) for SE
            if (seTargets.Count > 0)
            {
                ConsoleUI.PrintStep($"T3-SE: {string.Join(" ", seTargets)}");
                survivorAt = await SearchWithTransporterAsync(
                    passengers: Math.Min(2, seTargets.Count),
                    dropOff: "I9",
                    targets: seTargets);
                if (survivorAt != null) goto Evacuate;
            }
        }

        _logger.LogError("RunAsync", "Survivor not found in any block3 cell.");
        return "ERROR: Survivor not found in block3 cells.";

    Evacuate:
        _logger.LogPhase(3, "Evacuation");
        ConsoleUI.PrintPhase(3, "Evacuation");
        ConsoleUI.PrintStep($"Survivor at {survivorAt}! Calling helicopter...");
        var helicopterResp = await _tools.CallHelicopter(survivorAt);
        _logger.LogInfo($"Helicopter response: {helicopterResp}");
        ConsoleUI.PrintInfo($"Budget remaining: {_actionPointsLeft}");
        return helicopterResp;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core search loop
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string?> SearchWithTransporterAsync(
        int passengers, string dropOff, List<string> targets)
    {
        int createCost = 5 + 5 * passengers;

        if (_actionPointsLeft < createCost + targets.Count * 8)
        {
            ConsoleUI.PrintInfo($"Budget too low ({_actionPointsLeft}pt) for this cluster. Skipping.");
            return null;
        }

        // Create transporter with scouts
        var createResp = await _tools.CreateTransporter(passengers);
        _logger.LogInfo($"Create transporter({passengers}): {createResp}");
        UpdateBudget(createResp);

        var (transporterHash, crewIds) = ParseCreateResponse(createResp);
        if (string.IsNullOrEmpty(transporterHash))
        {
            _logger.LogError("SearchWithTransporterAsync", $"No transporter hash in: {createResp}");
            ConsoleUI.PrintError("Failed to parse transporter hash.");
            return null;
        }

        ConsoleUI.PrintInfo($"Transporter {Short(transporterHash)}, {crewIds.Count} scouts, budget: {_actionPointsLeft}");

        // Move transporter to drop-off
        var moveResp = await _tools.MoveUnit(transporterHash, dropOff);
        _logger.LogInfo($"Move transporter to {dropOff}: {moveResp}");
        UpdateBudget(moveResp);
        ConsoleUI.PrintInfo($"At {dropOff}, budget: {_actionPointsLeft}");

        // Dismount all scouts
        var dismountResp = await _tools.Dismount(transporterHash, passengers);
        _logger.LogInfo($"Dismount {passengers}: {dismountResp}");
        // Dismount is free

        // Get current unit state to find scout positions/IDs
        var objectsResp = await _tools.GetObjects();
        _logger.LogInfo($"GetObjects: {objectsResp}");

        // Use crew IDs from create (primary) or re-parse from getObjects (fallback)
        var scoutHashes = crewIds.Count > 0
            ? crewIds
            : ParseScoutHashesFromObjects(objectsResp, transporterHash);

        if (scoutHashes.Count == 0)
        {
            _logger.LogError("SearchWithTransporterAsync", "No scout hashes found after dismount.");
            ConsoleUI.PrintError("No scout hashes. Trying crew IDs from create response.");
            return null;
        }

        ConsoleUI.PrintInfo($"Scouts: {string.Join(", ", scoutHashes.Select(Short))}");

        // Search targets round-robin across scouts
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var scoutHash = scoutHashes[i % scoutHashes.Count];

            if (_actionPointsLeft < 8)
            {
                ConsoleUI.PrintInfo($"Budget critically low ({_actionPointsLeft}pt). Stopping.");
                break;
            }

            ConsoleUI.PrintStep($"Scout {Short(scoutHash)} → {target}");

            // Move scout to target building
            var scoutMoveResp = await _tools.MoveUnit(scoutHash, target);
            _logger.LogInfo($"Scout move to {target}: {scoutMoveResp}");
            UpdateBudget(scoutMoveResp);

            if (IsError(scoutMoveResp))
            {
                ConsoleUI.PrintError($"Move to {target} failed: {Truncate(scoutMoveResp, 200)}");
                // Try with crew[0].id fallback if move failed (wrong hash type)
                if (crewIds.Count > 0 && !crewIds.Contains(scoutHash))
                {
                    scoutHash = crewIds[i % crewIds.Count];
                    scoutMoveResp = await _tools.MoveUnit(scoutHash, target);
                    _logger.LogInfo($"Scout move retry with crew ID: {scoutMoveResp}");
                    UpdateBudget(scoutMoveResp);
                }
            }

            // Inspect current field
            var inspectResp = await _tools.InspectField(scoutHash);
            _logger.LogInfo($"Inspect {target}: {inspectResp}");
            UpdateBudget(inspectResp);
            ConsoleUI.PrintInfo($"Inspect result: {Truncate(inspectResp, 200)}, budget: {_actionPointsLeft}");

            // Check logs for survivor confirmation — parse each entry individually
            var logsResp = await _tools.GetLogs();
            _logger.LogInfo($"GetLogs after {target}: {logsResp}");

            var survivorField = FindSurvivorFieldInLogs(logsResp);
            if (survivorField != null)
            {
                ConsoleUI.PrintStep($"*** SURVIVOR CONFIRMED at {survivorField}! (inspected {target}) ***");
                _logger.LogInfo($"SURVIVOR FOUND at {survivorField}");
                return survivorField;
            }
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Response parsing
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the `create` response.
    /// Returns (objectHash, crewIds) where objectHash = "object" field and crewIds = crew[*].id.
    /// </summary>
    private static (string objectHash, List<string> crewIds) ParseCreateResponse(string response)
    {
        string objectHash = "";
        var crewIds = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // "object" field = main unit hash (transporter or standalone scout container)
            if (root.TryGetProperty("object", out var objEl) && objEl.ValueKind == JsonValueKind.String)
                objectHash = objEl.GetString() ?? "";

            // "crew" field = array of people inside
            if (root.TryGetProperty("crew", out var crewEl) && crewEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var member in crewEl.EnumerateArray())
                {
                    if (member.TryGetProperty("id", out var idEl))
                        crewIds.Add(idEl.GetString() ?? "");
                }
            }
        }
        catch (Exception ex)
        {
            // Not JSON or unexpected format
            Console.Error.WriteLine($"ParseCreateResponse error: {ex.Message}");
        }

        return (objectHash, crewIds);
    }

    /// <summary>
    /// Parses the `getObjects` response to find scout hashes (excluding the given transporter).
    /// </summary>
    private static List<string> ParseScoutHashesFromObjects(string response, string excludeTransporterHash)
    {
        var hashes = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // Try "objects" array
            JsonElement arr = default;
            foreach (var field in new[] { "objects", "units", "data", "result" })
            {
                if (root.TryGetProperty(field, out var el) && el.ValueKind == JsonValueKind.Array)
                {
                    arr = el;
                    break;
                }
            }

            if (arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var unit in arr.EnumerateArray())
                {
                    string? type = null;
                    string? id = null;

                    // API uses "typ" (not "type") — check both
                    if (unit.TryGetProperty("typ", out var typEl))
                        type = typEl.GetString();
                    else if (unit.TryGetProperty("type", out var typeEl))
                        type = typeEl.GetString();

                    if (unit.TryGetProperty("id", out var idEl))
                        id = idEl.GetString();
                    else if (unit.TryGetProperty("object", out var objEl))
                        id = objEl.GetString();

                    if (type?.Contains("scout", StringComparison.OrdinalIgnoreCase) == true
                        && !string.IsNullOrEmpty(id)
                        && id != excludeTransporterHash)
                    {
                        hashes.Add(id);
                    }
                }
            }
        }
        catch { }
        return hashes;
    }

    /// <summary>
    /// Parses a list of grid coordinates from a searchSymbol response.
    /// The API returns: { "found": [{"symbol": "B3", "position": "F1"}, ...] }
    /// We extract only the "position" field from each found entry.
    /// </summary>
    private static List<string> ParseCoordList(string response)
    {
        var coords = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // searchSymbol format: { "found": [{"symbol": "B3", "position": "F1"}, ...] }
            if (root.TryGetProperty("found", out var foundArr) && foundArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in foundArr.EnumerateArray())
                {
                    // Only read "position" field — never "symbol" (which looks like a coord but isn't)
                    if (item.TryGetProperty("position", out var pos) && IsValidCoord(pos.GetString()))
                        coords.Add(pos.GetString()!);
                }
                if (coords.Count > 0) return coords.Distinct().ToList();
            }

            // Fallback: look for any array field containing position-like objects
            foreach (var field in new[] { "fields", "coords", "positions", "result", "data", "locations" })
            {
                if (!root.TryGetProperty(field, out var el)) continue;

                if (el.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in el.EnumerateArray())
                    {
                        string? coord = null;
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            coord = item.GetString();
                        }
                        else if (item.ValueKind == JsonValueKind.Object)
                        {
                            // Prefer "position" over other fields to avoid picking up symbol names
                            foreach (var coordField in new[] { "position", "coord", "field", "location" })
                            {
                                if (item.TryGetProperty(coordField, out var cf) && cf.ValueKind == JsonValueKind.String)
                                {
                                    coord = cf.GetString();
                                    break;
                                }
                            }
                        }

                        if (IsValidCoord(coord))
                            coords.Add(coord!);
                    }

                    if (coords.Count > 0) break;
                }
            }
        }
        catch { }

        return coords.Distinct().ToList();
    }

    private static bool IsValidCoord(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length < 2 || s.Length > 3) return false;
        char col = char.ToUpper(s[0]);
        if (col < 'A' || col > 'K') return false;
        if (!int.TryParse(s[1..], out int row)) return false;
        return row >= 1 && row <= 11;
    }

    /// <summary>
    /// Parses getLogs response and finds the first log entry whose message confirms a survivor.
    /// Returns the "field" coordinate if found, or null if no survivor confirmed yet.
    /// </summary>
    private static string? FindSurvivorFieldInLogs(string logsResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(logsResponse);
            var root = doc.RootElement;
            if (!root.TryGetProperty("logs", out var logsArr) || logsArr.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var entry in logsArr.EnumerateArray())
            {
                string? msg = entry.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() : null;
                string? field = entry.TryGetProperty("field", out var fieldEl) ? fieldEl.GetString() : null;

                if (msg != null && field != null && IsPositiveLogEntry(msg))
                    return field;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Checks if a single inspect log message indicates the survivor was found.
    /// Negative messages dominate ("brak", "nieobecny", "nie ma", "puste").
    /// Positive: "Cel odnaleziony", person nouns (mężczyzna/kobieta), "żywy", etc.
    /// </summary>
    private static bool IsPositiveLogEntry(string msg)
    {
        var lower = msg.ToLowerInvariant();

        // Explicit negative patterns — common "empty" report phrases
        if (lower.Contains("brak") ||
            lower.Contains("nieobecny") ||
            lower.Contains("nie ma") ||
            lower.Contains("nie stwierdzono") ||
            lower.Contains("pusto") ||
            lower.Contains("puste") ||
            lower.Contains("opuszczone") ||
            lower.Contains("nikogo") ||
            lower.Contains("cel nieobecny") ||
            lower.Contains("nie widz") ||
            lower.Contains("kapanie"))
            return false;

        // Strong positive signals — target found / living person present
        return lower.Contains("cel odnaleziony") ||    // "Target found" — primary signal
               lower.Contains("znaleziono człowieka") ||
               lower.Contains("potwierdzono") ||
               lower.Contains("mężczyzna") ||          // man
               lower.Contains("kobieta") ||            // woman
               lower.Contains("człowiek") ||           // human
               lower.Contains("partyzant") ||
               lower.Contains("ocalały") ||
               lower.Contains("żywy") ||               // alive
               lower.Contains("żyje") ||               // is alive
               lower.Contains("siedział") ||           // was sitting (person action)
               lower.Contains("stał") ||               // was standing
               lower.Contains("leżał") ||              // was lying
               lower.Contains("reagował") ||           // reacted
               lower.Contains("odnaleziony");          // found/located
    }

    private static bool SurvivorFound(string response)
    {
        if (string.IsNullOrEmpty(response)) return false;
        return FindSurvivorFieldInLogs(response) != null;
    }

    private static bool IsError(string response)
    {
        if (string.IsNullOrEmpty(response)) return false;
        try
        {
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("code", out var code) && code.GetInt32() < 0)
                return true;
        }
        catch { }
        return response.StartsWith("HTTP ") || response.StartsWith("ERROR:");
    }

    private void UpdateBudget(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            foreach (var field in new[] { "action_points_left", "points_left", "budget_left", "remaining" })
            {
                if (root.TryGetProperty(field, out var el) && el.TryGetInt32(out int pts))
                {
                    _actionPointsLeft = pts;
                    return;
                }
            }
        }
        catch { }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Coordinate helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static int GetColIdx(string coord) => char.ToUpper(coord[0]) - 'A';
    private static int GetRow(string coord) => int.TryParse(coord[1..], out int r) ? r : 0;

    private static string Short(string hash) =>
        hash.Length >= 8 ? hash[..8] + ".." : hash;

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "...";
}
