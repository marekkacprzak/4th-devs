var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.OkoEditor>("oko-editor")
    .WithHttpEndpoint(port: 5010);
builder.AddProject<Projects.OkoEditor2>("oko-editor-2")
    .WithHttpEndpoint(port: 5011);
builder.Build().Run();
