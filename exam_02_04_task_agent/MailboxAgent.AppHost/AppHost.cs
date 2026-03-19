var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.MailboxAgent>("mailbox-agent");

builder.Build().Run();
