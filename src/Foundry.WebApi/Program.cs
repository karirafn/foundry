using Foundry.WebApi.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<FoundryDbContext>(
    "foundry",
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();
