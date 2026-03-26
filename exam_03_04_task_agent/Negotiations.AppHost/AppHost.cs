var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Negotiations>("reactor-agent");

builder.Build().Run();
