var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.CategorizeAgent>("categorize-agent");

builder.Build().Run();
