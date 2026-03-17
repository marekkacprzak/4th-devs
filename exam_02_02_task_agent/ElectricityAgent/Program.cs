using Microsoft.Extensions.Configuration;
using ElectricityAgent.Config;
using ElectricityAgent.Models;
using ElectricityAgent.Services;
using ElectricityAgent.Telemetry;
using ElectricityAgent.UI;

// 1. Load configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var hubConfig = new HubConfig();
configuration.GetSection("Hub").Bind(hubConfig);

// 2. Create services
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var hubApi = new HubApiClient(httpClient, hubConfig);

ConsoleUI.PrintBanner("ELECTRICITY", "Solve the cable puzzle");

int stepCounter = 0;

const int MaxAttempts = 3;
for (int attempt = 1; attempt <= MaxAttempts; attempt++)
{
    // --- STEP 1: Fetch board state (reset on first attempt) ---
    ConsoleUI.PrintStep($"ATTEMPT {attempt}/{MaxAttempts} - STEP 1: Fetch board state");
    var boardImageBytes = await hubApi.GetBoardImageAsync(reset: attempt == 1);
    ConsoleUI.PrintInfo($"Board image fetched: {boardImageBytes.Length} bytes");

    // Use programmatic pixel analysis — saves PNG + recognition report to data/
    stepCounter++;
    var boardState = ImageAnalyzer.AnalyzeBoard(boardImageBytes, stepCounter);
    ConsoleUI.PrintBoard(boardState.ToTextDescription());
    ConsoleUI.PrintBoard(boardState.ToGridView());

    // --- STEP 2: Deterministic solver ---
    ConsoleUI.PrintStep("STEP 2: Solve puzzle (deterministic backtracking)");

    var rotations = PuzzleSolver.Solve(boardState);
    ConsoleUI.PrintInfo($"Solver found {rotations.Count} tile(s) to rotate");

    // --- STEP 3: Execute rotations ---
    ConsoleUI.PrintStep("STEP 3: Execute rotations");

    if (rotations.Count == 0)
    {
        ConsoleUI.PrintInfo("No rotations needed — board already solved or no solution found.");
        continue;
    }

    ConsoleUI.PrintInfo($"Rotation plan: {rotations.Count} tiles to rotate");
    foreach (var (tile, count) in rotations)
    {
        ConsoleUI.PrintInfo($"  {tile}: {count} rotation(s)");
    }

    foreach (var (tile, count) in rotations)
    {
        for (int i = 0; i < count; i++)
        {
            ConsoleUI.PrintToolCall("RotateTile", $"{tile} ({i + 1}/{count})");
            var result = await hubApi.RotateTileAsync(tile);

            if (result.Contains("{FLG:"))
            {
                ConsoleUI.PrintResult($"SUCCESS! {result}");

                // Fetch final board state after solving
                ConsoleUI.PrintStep("STEP 4: Fetch solved board state");
                var finalImageBytes = await hubApi.GetBoardImageAsync(reset: false);
                ConsoleUI.PrintInfo($"Final board image fetched: {finalImageBytes.Length} bytes");
                stepCounter++;
                var finalBoardState = ImageAnalyzer.AnalyzeBoard(finalImageBytes, stepCounter);
                ConsoleUI.PrintBoard(finalBoardState.ToTextDescription());
                ConsoleUI.PrintBoard(finalBoardState.ToGridView());
                return;
            }
        }
    }

    ConsoleUI.PrintInfo("Rotations executed. Re-checking board...");
}

ConsoleUI.PrintError("Max attempts reached. Puzzle not solved.");
