using Foundry.WebApi.Shared.Persistence;

using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDbContext<FoundryDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("foundry") ?? "Data Source=data/foundry.db"));
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();
