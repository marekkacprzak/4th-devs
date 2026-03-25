var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.ReactorAgent>("reactor-agent");

builder.Build().Run();
