var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.FailureAgent>("failure-agent");

builder.Build().Run();
