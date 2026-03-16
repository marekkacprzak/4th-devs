var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.FindHimAgent>("find-him-agent");

builder.Build().Run();
