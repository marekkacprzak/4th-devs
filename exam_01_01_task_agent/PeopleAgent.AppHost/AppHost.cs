var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.PeopleAgent>("people-agent");

builder.Build().Run();
