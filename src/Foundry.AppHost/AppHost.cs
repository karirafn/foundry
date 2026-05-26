IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<PostgresDatabaseResource> postgres = builder
    .AddPostgres("foundry-server")
    .AddDatabase("foundry");

builder
    .AddProject<Projects.Foundry_WebApi>("webapi")
    .WithReference(postgres);

builder.Build().Run();
