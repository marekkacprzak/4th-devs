var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.WindPower>("wind-power").WithHttpEndpoint(port: 5010);
builder.Build().Run();
