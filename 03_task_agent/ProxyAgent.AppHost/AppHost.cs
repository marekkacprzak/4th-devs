var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.ProxyAgent>("proxy-agent");

builder.Build().Run();
