using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoingThere.Services;
using GoingThere.UI;

namespace GoingThere.Tools;

public class GameTools
{
    private readonly HubApiClient _hub;
    private readonly RunLogger _logger;

    // Game state — updated from every API response
    private int _currentRow = 2;
    private int _currentCol = 1;
    private int _targetRow = 1;
    // Stone in the CURRENT column (from API response). Moving left/right within the column
    // first moves us vertically — if that row equals _currentStoneRow we crash immediately.
    private int _currentStoneRow = -1; // -1 = unknown

    // Crash learning: remember (col:row:direction) moves that crashed, so we try alternatives
    private readonly HashSet<string> _crashedMoves = new();

    private enum RockDirection { Ahead, Port, Starboard, Unknown }

    public GameTools(HubApiClient hub, RunLogger logger)
    {
        _hub = hub;
        _logger = logger;
    }

    [Description("Start a new game. Resets game state and returns starting position and target base location. Call this once before NavigateAllColumns.")]
    public async Task<string> StartGame()
    {
        ConsoleUI.PrintStep("StartGame");
        _logger.LogInfo("Tool: StartGame");
        var result = await _hub.SendCommandAsync("start");
        UpdateStateFromResponse(result);
        _logger.LogInfo($"StartGame: row={_currentRow} col={_currentCol} targetRow={_targetRow}");
        return result;
    }

    [Description("Navigate the rocket through all 11 columns to reach the base at column 12. Automatically handles radar checks, disarming traps, parsing radio hints, and choosing safe movement directions. Returns the flag when the base is reached.")]
    public async Task<string> NavigateAllColumns()
    {
        ConsoleUI.PrintStep("NavigateAllColumns: starting programmatic navigation");
        _logger.LogInfo($"NavigateAllColumns: start row={_currentRow} col={_currentCol} targetRow={_targetRow}");

        int maxCrashes = 50;
        int crashes = 0;
        int maxStepsPerGame = 20;
        // Small cooldown between crash restarts (rate limit resets after ~1 minute per user feedback,
        // and 2s between all calls is sufficient to avoid hitting it).
        const int CrashCooldownMs = 2000;

        while (crashes < maxCrashes)
        {
            var result = await NavigateGame(maxStepsPerGame);

            if (result.StartsWith("FLAG:") || result.StartsWith("SUCCESS"))
                return result;

            if (result.StartsWith("CRASH:"))
            {
                crashes++;
                _logger.LogError("Navigate", $"Crash #{crashes}: {result}. Restarting...");
                ConsoleUI.PrintError($"Crash #{crashes} — restarting game... (cooldown {CrashCooldownMs}ms)");

                await Task.Delay(CrashCooldownMs);
                var restart = await _hub.SendCommandAsync("start");
                UpdateStateFromResponse(restart);
                continue;
            }

            // Any other error — return it
            return result;
        }

        return $"ERROR: Crashed {maxCrashes} times without reaching the base.";
    }

    private async Task<string> NavigateGame(int maxSteps)
    {
        for (int step = 0; step < maxSteps; step++)
        {
            ConsoleUI.PrintInfo($"Step {step + 1}: col={_currentCol} row={_currentRow} target={_targetRow}");
            _logger.LogInfo($"Navigation step {step + 1}: col={_currentCol} row={_currentRow} targetRow={_targetRow}");

            // ── 1. Check radar ────────────────────────────────────────────────────────
            var radarResult = await CheckAndDisarmRadarInternal();
            _logger.LogInfo($"Radar result: {radarResult}");
            // Network errors (502, timeout) on the scanner are transient — treat as CLEAR
            // so navigation continues rather than aborting the whole game.
            if (radarResult.StartsWith("ERROR:"))
            {
                _logger.LogInfo("Radar scanner error — treating as CLEAR to continue navigation");
                radarResult = "CLEAR (scanner error)";
            }

            // ── 2. Get radio hint ─────────────────────────────────────────────────────
            var hint = await GetHintInternal();
            _logger.LogInfo($"Hint: {hint}");
            ConsoleUI.PrintInfo($"Hint: {hint}");

            // ── 3. Parse hint & compute safe direction ────────────────────────────────
            var rockDir = ParseHintDirection(hint);
            var direction = ComputeSafeDirection(rockDir);
            _logger.LogInfo($"Rock at {rockDir} → moving {direction}");
            ConsoleUI.PrintInfo($"Rock at {rockDir} → moving '{direction}'");

            // ── 4. Move ───────────────────────────────────────────────────────────────
            var moveResult = await _hub.SendCommandAsync(direction);
            _logger.LogInfo($"Move '{direction}' result: {moveResult}");

            // ── 5. Interpret move result ──────────────────────────────────────────────
            if (IsCrash(moveResult))
            {
                // Record the crashed move so we try the alternative next time from this position
                var crashKey = $"{_currentCol}:{_currentRow}:{direction}";
                _crashedMoves.Add(crashKey);
                _logger.LogInfo($"Recorded crashed move: {crashKey}");
                return $"CRASH: moved {direction} from row {_currentRow} col {_currentCol} (rock at {rockDir}). Response: {moveResult}";
            }

            if (IsFlag(moveResult))
            {
                ConsoleUI.PrintResult($"FLAG FOUND! {moveResult}");
                return $"FLAG: {moveResult}";
            }

            UpdateStateFromResponse(moveResult);
            _logger.LogInfo($"After move: col={_currentCol} row={_currentRow}");

            if (_currentCol >= 12)
                return $"SUCCESS: Reached column 12. {moveResult}";
        }

        return $"ERROR: Reached max steps ({maxSteps}) without completing navigation.";
    }

    // ── Internal helpers ─────────────────────────────────────────────────────────────

    private async Task<string> CheckAndDisarmRadarInternal()
    {
        var scannerResponse = await _hub.CheckFrequencyScannerAsync();

        if (scannerResponse.StartsWith("ERROR:"))
            return scannerResponse;

        // Structural detection: clear responses are plain strings ("It's clear!", "cleeeeeear")
        // Trap responses are JSON objects beginning with '{'.
        // Key names are corrupted (e.g. "frepuency", "betecti0nC0be") — never match by key name.
        var trimmed = scannerResponse.Trim();
        if (!trimmed.StartsWith('{'))
        {
            _logger.LogInfo("Radar: CLEAR (not a JSON object)");
            return "CLEAR";
        }

        // JSON object → trap. Extract values by pattern, not by key name.
        ConsoleUI.PrintInfo("Radar trap detected — extracting values from corrupted JSON...");

        // Frequency: first integer value >= 50 in the JSON (e.g. "frepuency": 185)
        int frequency = 0;
        foreach (Match m in Regex.Matches(trimmed, @":\s*(\d{2,5})\b"))
        {
            if (int.TryParse(m.Groups[1].Value, out int n) && n >= 50)
            {
                frequency = n;
                break;
            }
        }

        // DetectionCode: first string VALUE matching an alphanumeric code pattern
        // (mixed case OR contains digits, 4-20 chars — e.g. "DYTtZn", 'eBu21Y", 'Tptryp")
        // Handles corrupted JSON with mixed/single quotes as value delimiters
        string detectionCode = "";
        foreach (Match m in Regex.Matches(trimmed, ":\\s*[\"']([A-Za-z0-9]{4,20})[\"']"))
        {
            var val = m.Groups[1].Value;
            if (val.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                val.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                val.Equals("null", StringComparison.OrdinalIgnoreCase))
                continue;
            bool hasMixedCase = val.Any(char.IsUpper) && val.Any(char.IsLower);
            bool hasDigits = val.Any(char.IsDigit);
            if (hasMixedCase || hasDigits)
            {
                detectionCode = val;
                break;
            }
        }

        if (frequency == 0 || string.IsNullOrEmpty(detectionCode))
        {
            _logger.LogError("Radar", $"Trap JSON but extraction failed. freq={frequency}, code='{detectionCode}'. Raw: {trimmed}");
            // Treat as clear to avoid blocking — better to proceed than to hang
            return "CLEAR (extraction failed)";
        }
        var disarmHash = Convert.ToHexStringLower(
            SHA1.HashData(Encoding.UTF8.GetBytes(detectionCode + "disarm")));

        _logger.LogInfo($"Disarming: freq={frequency} code={detectionCode} hash={disarmHash}");
        ConsoleUI.PrintInfo($"Disarming radar: freq={frequency}");

        var disarmResult = await _hub.DisarmRadarAsync(frequency, disarmHash);

        if (disarmResult.StartsWith("ERROR:") || disarmResult.StartsWith("HTTP 4"))
            return $"ERROR: Disarm failed: {disarmResult}";

        _logger.LogInfo($"Disarmed successfully: {disarmResult}");
        return "DISARMED";
    }

    // Navigation keywords that must appear in a valid hint
    private static readonly string[] NavigationKeywords = {
        "port", "starboard", "ahead", "forward", "rock", "stone", "clear", "safe",
        "left", "right", "flank", "side", "hull", "cockpit", "heading", "threat",
        "hazard", "obstacle", "obstruction", "danger", "free", "open", "clean", "empty"
    };

    private async Task<string> GetHintInternal()
    {
        var response = await _hub.GetRadioHintAsync();
        try
        {
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("hint", out var hintProp))
            {
                var hint = hintProp.GetString() ?? response;
                // Detect rate-limit / non-navigation responses (e.g. Polish "Za często wykonujesz zapytania")
                var hintLower = hint.ToLowerInvariant();
                bool hasNavKeyword = NavigationKeywords.Any(k => hintLower.Contains(k));
                if (!hasNavKeyword)
                    return $"ERROR: non-navigation hint: {hint}";
                return hint;
            }
        }
        catch (JsonException) { }
        return response;
    }

    private RockDirection ParseHintDirection(string hint)
    {
        // If hint retrieval failed entirely, we have no information — go straight
        if (hint.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            return RockDirection.Unknown;

        var h = hint.ToLowerInvariant();

        // Handle "opposite" cases: "opposite starboard" = port, "opposite port" = starboard
        if (h.Contains("opposite starboard") || h.Contains("other side of starboard"))
            return RockDirection.Port;
        if (h.Contains("opposite port") || h.Contains("other side of port"))
            return RockDirection.Starboard;

        // ── Explicit "direction = bad" patterns ──────────────────────────────────
        // "watch X" = pay attention to X = danger; add watch/fear (non-negated) as danger signals
        var dangerWords = @"(distrust|risk|bad|threat|avoid|danger|hazard|rock|obstacle|obstruction|trouble|solid|problem|blocked|crowd|press|sits|sitting|planted|posted|occupied|carries|carrying|issue|warn|watch)";
        // Use [^.!?]{0,40} to avoid matching across sentence boundaries
        if (Regex.IsMatch(h, @"\bstarboard\b[^.!?]{0,40}" + dangerWords) ||
            Regex.IsMatch(h, dangerWords + @"[^.!?]{0,40}\bstarboard\b"))
        {
            // Make sure port isn't also labelled dangerous in the same tight window
            bool portDanger = Regex.IsMatch(h, @"\bport\b[^.!?]{0,40}" + dangerWords) ||
                              Regex.IsMatch(h, dangerWords + @"[^.!?]{0,40}\bport\b");
            if (!portDanger) return RockDirection.Starboard;
        }

        if (Regex.IsMatch(h, @"\bport\b[^.!?]{0,40}" + dangerWords) ||
            Regex.IsMatch(h, dangerWords + @"[^.!?]{0,40}\bport\b"))
        {
            bool starboardDanger = Regex.IsMatch(h, @"\bstarboard\b[^.!?]{0,40}" + dangerWords) ||
                                   Regex.IsMatch(h, dangerWords + @"[^.!?]{0,40}\bstarboard\b");
            if (!starboardDanger) return RockDirection.Port;
        }

        // ── Safe-direction inference ──────────────────────────────────────────────
        // If both port AND starboard are indicated safe → rock is Ahead
        // safeWords: explicit positive words OR negated-fear patterns ("not fear", "no need to fear")
        var safeWords = @"(clear|open|free|safe|clean|empty|nothing|no issue|no threat|no hazard|usable)";
        var notFearPattern = @"(not?\s+(?:need\s+to\s+)?fear|no\s+need\s+to\s+fear|don'?t\s+(?:need\s+to\s+)?fear)";

        bool portSafe = Regex.IsMatch(h, @"\bport\b[^.!?]{0,40}" + safeWords) ||
                        Regex.IsMatch(h, safeWords + @"[^.!?]{0,40}\bport\b") ||
                        Regex.IsMatch(h, notFearPattern + @"[^.!?]{0,50}\bport\b") ||
                        Regex.IsMatch(h, @"\bport\b[^.!?]{0,50}" + notFearPattern);

        bool starboardSafe = Regex.IsMatch(h, @"\bstarboard\b[^.!?]{0,40}" + safeWords) ||
                             Regex.IsMatch(h, safeWords + @"[^.!?]{0,40}\bstarboard\b") ||
                             Regex.IsMatch(h, notFearPattern + @"[^.!?]{0,50}\bstarboard\b") ||
                             Regex.IsMatch(h, @"\bstarboard\b[^.!?]{0,50}" + notFearPattern);

        // "Both flanks" = both port AND starboard — if flanks are safe/usable, rock is ahead
        bool flanksSafe = Regex.IsMatch(h, @"\bflanks?\b[^.!?]{0,40}" + safeWords) ||
                          Regex.IsMatch(h, safeWords + @"[^.!?]{0,40}\bflanks?\b");

        if ((portSafe && starboardSafe) || flanksSafe)
            return RockDirection.Ahead;

        // ── Sentence-level scan with expanded hazard vocabulary ───────────────────
        var hazardKeywords = new[] {
            "hazard", "rock", "obstacle", "obstruction", "trouble", "danger", "solid", "problem",
            "blocked", "distrust", "risk", "threat", "bad", "avoid", "occupied", "crowding",
            "pressing", "sits", "sitting", "planted", "posted", "carries", "carrying", "issue"
        };
        var sentences = Regex.Split(h, @"[.!?]")
            .Select(s => s.Trim())
            .Where(s => s.Length > 5)
            .ToArray();

        // Check sentences in reverse (last sentence is usually the hazard location)
        foreach (var sentence in sentences.Reverse())
        {
            if (!hazardKeywords.Any(k => sentence.Contains(k)))
                continue;

            // Skip sentences with strong negation of a direction (e.g. "not toward starboard")
            bool negatedStarboard = Regex.IsMatch(sentence, @"\b(not|no|neither|nothing)\b.{0,20}\b(starboard|right)\b");
            bool negatedPort = Regex.IsMatch(sentence, @"\b(not|no|neither|nothing)\b.{0,20}\b(port|left)\b");

            if (!negatedStarboard && (sentence.Contains("starboard") || sentence.Contains("right side") || sentence.Contains("right flank")))
                return RockDirection.Starboard;

            if (!negatedPort && (sentence.Contains("port") || sentence.Contains("left side") || sentence.Contains("left flank")))
                return RockDirection.Port;

            // No direction word (or all negated) → rock is ahead (same row)
            return RockDirection.Ahead;
        }

        // Default: rock is ahead (most common pattern)
        return RockDirection.Ahead;
    }

    // Choose between two options, preferring the one not previously crashed, then target-aligned
    private string PickDirection(string preferred, string alternative)
    {
        // Compute the row we'd end up in for each direction (within current column)
        int RowAfter(string dir) => dir switch { "left" => _currentRow - 1, "right" => _currentRow + 1, _ => _currentRow };

        // If moving in 'preferred' direction would land on the current column's stone, it's blocked
        bool prefBlockedByCurrentStone  = _currentStoneRow >= 1 && RowAfter(preferred)  == _currentStoneRow;
        bool altBlockedByCurrentStone   = _currentStoneRow >= 1 && RowAfter(alternative) == _currentStoneRow;

        var prefKey = $"{_currentCol}:{_currentRow}:{preferred}";
        var altKey  = $"{_currentCol}:{_currentRow}:{alternative}";
        bool prefCrashed = _crashedMoves.Contains(prefKey);
        bool altCrashed  = _crashedMoves.Contains(altKey);

        // Stone in current column physically blocks this direction
        if (prefBlockedByCurrentStone && !altBlockedByCurrentStone)
        {
            _logger.LogInfo($"Current-stone block: {preferred}(→row{RowAfter(preferred)}) blocked by stoneRow={_currentStoneRow}, using {alternative}");
            return alternative;
        }
        // Past crash history: if preferred crashed before and alt hasn't, swap
        if (prefCrashed && !prefBlockedByCurrentStone && !altCrashed)
        {
            _logger.LogInfo($"Crash-avoidance: swapping {preferred} → {alternative} at col={_currentCol} row={_currentRow}");
            return alternative;
        }
        return preferred;
    }

    private string ComputeSafeDirection(RockDirection rockDir)
    {
        switch (rockDir)
        {
            case RockDirection.Ahead:
                // Rock is at the same row as us — must change row
                if (_currentRow == 1) return PickDirection("right", "go");  // boundary: prefer right but "go" is last resort
                if (_currentRow == 3) return PickDirection("left", "go");   // boundary
                // Row 2: prefer direction toward target, but flip if that crashed before
                return _targetRow <= 1
                    ? PickDirection("left", "right")
                    : PickDirection("right", "left");

            case RockDirection.Port:
                // Rock is above (row - 1) — safe to stay or go down
                if (_currentRow == 3) return "go";
                return _targetRow > _currentRow
                    ? PickDirection("right", "go")
                    : PickDirection("go", "right");

            case RockDirection.Starboard:
                // Rock is below (row + 1) — safe to stay or go up
                if (_currentRow == 1) return "go";
                return _targetRow < _currentRow
                    ? PickDirection("left", "go")
                    : PickDirection("go", "left");

            default:
                // Unknown hint — move toward target
                if (_currentRow == _targetRow) return PickDirection("go", _targetRow == 1 ? "right" : "left");
                return _targetRow < _currentRow
                    ? PickDirection("left", "go")
                    : PickDirection("right", "go");
        }
    }

    private void UpdateStateFromResponse(string response)
    {
        try
        {
            // Strip HTTP prefix if present
            var json = response.StartsWith("HTTP ") ? response[(response.IndexOf(':') + 1)..].Trim() : response;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("player", out var player))
            {
                if (player.TryGetProperty("row", out var row)) _currentRow = row.GetInt32();
                if (player.TryGetProperty("col", out var col)) _currentCol = col.GetInt32();
            }
            if (root.TryGetProperty("base", out var basePos))
            {
                if (basePos.TryGetProperty("row", out var bRow)) _targetRow = bRow.GetInt32();
            }
            // Track stone in current column — moving left/right within this column
            // moves us vertically BEFORE advancing; if target row == stoneRow we crash.
            if (root.TryGetProperty("currentColumn", out var curCol))
            {
                if (curCol.TryGetProperty("stoneRow", out var sRow))
                    _currentStoneRow = sRow.GetInt32();
            }
        }
        catch { /* ignore parse errors, keep last known state */ }
    }

    private static bool IsCrash(string response) =>
        response.Contains("\"crashed\"") ||
        response.Contains("crashed\":true") ||
        response.Contains("crashed\": true") ||
        (response.Contains("HTTP 400") && response.Contains("crashed"));

    private static bool IsFlag(string response) =>
        response.Contains("flag{", StringComparison.OrdinalIgnoreCase) ||
        response.Contains("{{FLG", StringComparison.OrdinalIgnoreCase) ||
        response.Contains("FLG:", StringComparison.OrdinalIgnoreCase) ||
        response.Contains("\"flag\"", StringComparison.OrdinalIgnoreCase) ||
        response.Contains("congratulation", StringComparison.OrdinalIgnoreCase) ||
        (response.Contains("\"code\"") && response.Contains("200") && response.Contains("col") && response.Contains("12"));
}
