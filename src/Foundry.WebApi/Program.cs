using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

const string AngularDevServerPolicy = "AngularDevServer";
const string DocGenerationEntryAssemblyName = "GetDocument.Insider";

// GetDocument.Insider is the build-time OpenAPI doc generation tool entry point.
// When running under it, skip non-essential startup logic that requires a live database or filesystem.
bool isDocGeneration = Assembly.GetEntryAssembly()?.GetName().Name == DocGenerationEntryAssemblyName;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.Converters.Add(new WorkerRunIdJsonConverter());
});

if (!isDocGeneration)
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo("data/dp-keys"));
}

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

if (!isDocGeneration)
{
    builder.Services.AddHostedService<OutboxRelayService>();
}

builder.Services.AddCredentialsModule();
builder.Services.AddIssuesModule();
builder.Services.AddMonitoringModule();
builder.Services.AddWorkersModule(builder.Configuration);
builder.Services.AddSettingsModule();
builder.Services.AddOpenApi(options =>
{
    // Qualifies nested schema types by their outermost declaring type (e.g. "OuterSimpleName").
    // Collision-free as long as no two distinct nested endpoint DTO types share the same
    // outermost.Name + simpleName combination. A future collision would surface immediately
    // in the regenerated v1.json diff caught by CI.
    options.CreateSchemaReferenceId = (JsonTypeInfo jsonTypeInfo) =>
    {
        // WorkerRunIdJsonConverter flattens WorkerRunId to a bare UUID string on the wire.
        // Returning null here forces the schema to be inlined at each use site
        // as { type: "string", format: "uuid" } via the schema transformer below,
        // so the generated schema.ts keeps workerRunId: string (matching the wire shape).
        if (jsonTypeInfo.Type == typeof(WorkerRunId))
        {
            return null;
        }

        string? defaultId = OpenApiOptions.CreateDefaultSchemaReferenceId(jsonTypeInfo);
        if (defaultId is null)
        {
            return null;
        }

        Type outermost = jsonTypeInfo.Type;
        while (outermost.DeclaringType is Type declaring)
        {
            outermost = declaring;
        }

        return ReferenceEquals(outermost, jsonTypeInfo.Type)
            ? defaultId
            : outermost.Name + defaultId;
    };

    // WorkerRunIdJsonConverter flattens WorkerRunId to a bare UUID string on the wire.
    // This transformer ensures the inlined schema appears as { type: "string", format: "uuid" }
    // at every workerRunId field, preserving the wire shape visible to the Angular client.
    options.AddSchemaTransformer((OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct) =>
    {
        if (context.JsonTypeInfo.Type == typeof(WorkerRunId))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "uuid";
            schema.Properties?.Clear();
        }

        return Task.CompletedTask;
    });
});

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.Converters.Add(new WorkerRunIdJsonConverter()));
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
    if (!isDocGeneration)
    {
        using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        dbContext.Database.Migrate();
    }

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
