var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.Filesystem>("filesystem").WithHttpEndpoint(port: 5012);
builder.Build().Run();
