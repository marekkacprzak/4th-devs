var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.EvaluationAgent>("evaluation-agent");

builder.Build().Run();
