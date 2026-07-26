using Foundry.Modules.Credentials;
using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Issues;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring;
using Foundry.Modules.Settings;
using Foundry.Modules.Workers;
using Foundry.Modules.Workers.Contracts;
using Foundry.ServiceDefaults;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Hubs;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

const string AngularDevServerPolicy = "AngularDevServer";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("data/dp-keys"));
builder.Services.AddScoped<IntegrationEventCollector>();
builder.Services.AddScoped<OutboxSaveChangesInterceptor>();
builder.Services.AddDbContext<FoundryDbContext>((sp, options) =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("foundry") ?? "Data Source=data/foundry.db");
    options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
});
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
builder.Services.AddScoped<IIntegrationEventDispatcher, OutboxIntegrationEventDispatcher>();
builder.Services.AddScoped<IIntegrationEventProcessor, IntegrationEventProcessor>();
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
builder.Services.AddOutboxOptionsValidation();
builder.Services.AddHostedService<OutboxRelayService>();
builder.Services.AddCredentialsModule();
builder.Services.AddIssuesModule();
builder.Services.AddMonitoringModule(builder.Configuration);
builder.Services.AddWorkersModule(builder.Configuration);
builder.Services.AddSettingsModule();
builder.Services.AddOpenApi();

builder.Services.AddSignalR();
builder.Services.AddScoped<IIssueBroadcaster, SignalRIssueBroadcaster>();
builder.Services.AddSingleton<ISystemNotificationBroadcaster, SignalRSystemNotificationBroadcaster>();
builder.Services.AddSingleton<ILoginSessionBroadcaster, SignalRLoginSessionBroadcaster>();
builder.Services.AddScoped<IWorkerActivityBroadcaster, SignalRWorkerActivityBroadcaster>();

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

GlobalExceptionLogging.Install(app.Services.GetRequiredService<ILoggerFactory>());

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
app.MapCredentialsEndpoints();
app.MapIssuesEndpoints();
app.MapMonitoringEndpoints();
app.MapWorkersEndpoints();
app.MapSettingsEndpoints();
app.MapHub<IssueHub>("/hubs/issues");
app.MapHub<SystemNotificationHub>("/hubs/system");
app.MapHub<WorkerHub>("/hubs/workers");

app.Run();
