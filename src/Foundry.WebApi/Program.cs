using Foundry.Modules.Issues;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring;
using Foundry.Modules.Workers;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.WebApi.Hubs;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

const string AngularDevServerPolicy = "AngularDevServer";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("data/dp-keys"));
builder.Services.AddDbContext<FoundryDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("foundry") ?? "Data Source=data/foundry.db"));
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
builder.Services.AddScoped<IIntegrationEventDispatcher, IntegrationEventDispatcher>();
builder.Services.AddProviderAuth();
builder.Services.AddIssuesModule();
builder.Services.AddMonitoringModule(builder.Configuration);
builder.Services.AddWorkersModule(builder.Configuration);
builder.Services.AddOpenApi();

builder.Services.AddSignalR();
builder.Services.AddScoped<IIssueBroadcaster, SignalRIssueBroadcaster>();
builder.Services.AddScoped<IWorkerLogBroadcaster, SignalRWorkerLogBroadcaster>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(AngularDevServerPolicy, policy =>
    {
        policy
            .WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
    dbContext.Database.Migrate();

    app.UseCors(AngularDevServerPolicy);
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapDefaultEndpoints();
app.MapIssuesEndpoints();
app.MapMonitoringEndpoints();
app.MapWorkersEndpoints();
app.MapHub<IssueHub>("/hubs/issues");
app.MapHub<WorkerLogHub>("/hubs/worker-log");

app.Run();
