var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.RadioMonitoring>("radio-monitoring")
    .WithHttpEndpoint(port: 5020);
builder.Build().Run();
