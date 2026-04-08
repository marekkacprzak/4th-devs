var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.ShellAccess>("shell-access")
    .WithHttpEndpoint(port: 5040);
builder.Build().Run();
