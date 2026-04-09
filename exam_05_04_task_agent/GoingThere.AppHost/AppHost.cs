var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.GoingThere>("going-there")
    .WithHttpEndpoint(port: 5020);
builder.Build().Run();
