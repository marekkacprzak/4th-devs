var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.DroneAgent>("drone-agent");

builder.Build().Run();
