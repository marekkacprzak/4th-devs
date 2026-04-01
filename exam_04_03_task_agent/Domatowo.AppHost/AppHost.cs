var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.Domatowo>("domatowo").WithHttpEndpoint(port: 5010);
builder.Build().Run();
