var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.FirmwareAgent>("firmware-agent");

builder.Build().Run();
