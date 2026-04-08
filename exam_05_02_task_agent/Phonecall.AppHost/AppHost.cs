var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.Phonecall>("phonecall")
    .WithHttpEndpoint(port: 5030);
builder.Build().Run();
