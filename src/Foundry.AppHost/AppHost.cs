IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

builder
    .AddProject<Projects.Foundry_WebApi>("webapi");

builder.Build().Run();
