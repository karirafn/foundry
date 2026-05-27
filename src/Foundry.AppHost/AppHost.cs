IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

string dataDir = Path.Combine(builder.AppHostDirectory, "..", "..", "data");
string connectionString = $"Data Source={Path.Combine(dataDir, "foundry.db")}";

builder
    .AddProject<Projects.Foundry_WebApi>("webapi")
    .WithEnvironment("ConnectionStrings__foundry", connectionString);

builder.Build().Run();
