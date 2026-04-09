var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.Phonecall2>("phonecall2")
    .WithHttpEndpoint(port: 5030);
builder.Build().Run();
