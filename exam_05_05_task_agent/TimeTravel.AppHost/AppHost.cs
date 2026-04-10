var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.TimeTravel>("time-travel")
    .WithHttpEndpoint(port: 5020);
builder.Build().Run();
