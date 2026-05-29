using Foundry.WebApi.Modules.Issues;
using Foundry.WebApi.Modules.Monitoring;
using Foundry.WebApi.Modules.Workers;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDbContext<FoundryDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("foundry") ?? "Data Source=data/foundry.db"));
builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
builder.Services.AddIssuesModule();
builder.Services.AddMonitoringModule(builder.Configuration);
builder.Services.AddWorkersModule(builder.Configuration);
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.MapMonitoringEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();
