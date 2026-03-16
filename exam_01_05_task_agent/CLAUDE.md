# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is exercise 05 ("task agent") from the AI Devs 4th course. The task involves interacting with a railway route management API to reconfigure route statuses based on data in local files.

## Tech Stack

- **Language:** C#
- **Framework:** [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) (NuGet: `Microsoft.Agents.AI`, `Microsoft.Agents.AI.OpenAI`)
- **Target:** .NET 8.0+
- The agent should read local data files (CSV, JSON), interact with the railway API, and autonomously decide which routes to reconfigure

### Key Framework Patterns

```csharp
// Core packages:
// dotnet add package Microsoft.Agents.AI.OpenAI --prerelease
// dotnet add package Azure.AI.OpenAI --prerelease
// dotnet add package Azure.Identity

using Microsoft.Agents.AI;
using OpenAI.Responses;

// Agent with tools:
AIAgent agent = client.GetResponsesClient(deploymentName)
    .AsAIAgent(
        name: "AgentName",
        instructions: "...",
        tools: [AIFunctionFactory.Create(MyToolMethod)]);

// Run:
await agent.RunAsync("prompt");
// Streaming:
await foreach (var update in agent.RunStreamingAsync("prompt")) { ... }
```

## Key Files

- **task.md** - Full lesson content (Polish) covering production concerns for generative AI apps: context management, control, performance, cost, security, scaling
- **help.json** / **help_answer.json** - API interaction: the task name is `railway`, and the API exposes actions: `help`, `reconfigure`, `getstatus`, `setstatus`, `save`
- **trasy_wylaczone.csv** - Disabled railway routes (X-01 through X-08) with route codes, descriptions, reasons, and reactivation forecasts

## Railway API

API key: stored in `help.json` (`apikey` field). Task name: `railway`.

Workflow to change a route status:
1. `reconfigure` (route) - enter reconfigure mode
2. `setstatus` (route, value) - set status to `RTOPEN` or `RTCLOSE`
3. `save` (route) - exit reconfigure mode

Route format: `[a-z]-[0-9]{1,2}` (case-insensitive).

## Custom Commands (.claude/)

- **plan.md** - Systematic planning workflow with checkpoints (analyze -> research -> consult user -> plan -> save docs -> present)
- **implement.md** - Implementation workflow that reads a prior plan and executes step-by-step with rollback support

Both commands expect documentation output in `.\details\YYYY\MM\` and updates to `TODO.md`, `CHANGELOG.md`, and `INDEX.md`.
