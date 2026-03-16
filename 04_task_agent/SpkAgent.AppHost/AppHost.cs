var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.SpkAgent>("spk-agent");

builder.Build().Run();
