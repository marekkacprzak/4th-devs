var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.SavethemAgent>("savethem-agent");

builder.Build().Run();
