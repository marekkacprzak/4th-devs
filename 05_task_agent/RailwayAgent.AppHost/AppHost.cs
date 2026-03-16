var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.RailwayAgent>("railway-agent");

builder.Build().Run();
