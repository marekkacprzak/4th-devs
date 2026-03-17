var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.ElectricityAgent>("electricity-agent");

builder.Build().Run();
