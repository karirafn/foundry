IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

string dataDir = Path.Combine(builder.AppHostDirectory, "..", "..", "data");
string connectionString = $"Data Source={Path.Combine(dataDir, "foundry.db")}";

IResourceBuilder<ProjectResource> webapi = builder
    .AddProject<Projects.Foundry_WebApi>("webapi")
    .WithEnvironment("ConnectionStrings__foundry", connectionString);

// Optional webhook-only ingress port (ADR 0085). When "Webhooks:Port" is set in the AppHost
// configuration (e.g. user secrets or WEBHOOKS__PORT), the WebApi binds a second Kestrel endpoint on
// it that serves only /webhooks/*; expose that port through a tunnel or reverse proxy.
string? webhookPort = builder.Configuration["Webhooks:Port"];
if (!string.IsNullOrWhiteSpace(webhookPort))
{
    webapi.WithEnvironment("Webhooks__Port", webhookPort);
}

builder.AddJavaScriptApp("foundry-web", "../foundry-web", "start")
    .WithHttpEndpoint(port: 4200, env: "PORT", isProxied: false)
    .WithHttpHealthCheck(path: "/", statusCode: 200)
    .WithReference(webapi)
    .WaitFor(webapi);

builder.Build().Run();
