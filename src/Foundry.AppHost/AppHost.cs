IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

string dataDir = Path.Combine(builder.AppHostDirectory, "..", "..", "data");
string connectionString = $"Data Source={Path.Combine(dataDir, "foundry.db")}";

IResourceBuilder<ProjectResource> webapi = builder
    .AddProject<Projects.Foundry_WebApi>("webapi")
    .WithEnvironment("ConnectionStrings__foundry", connectionString);

// Image build only; runtime containers are dispatched by DockerWorkerOrchestrator
builder.AddDockerfile("foundry-worker", "../../workers")
    .WithImage("foundry-worker")
    .WithImageTag("local");

builder.AddJavaScriptApp("foundry-web", "../foundry-web", "start")
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WithReference(webapi)
    .WaitFor(webapi);

builder.Build().Run();
