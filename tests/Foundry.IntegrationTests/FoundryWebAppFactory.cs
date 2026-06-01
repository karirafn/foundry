using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Foundry.IntegrationTests;

public sealed class FoundryWebAppFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public FoundryWebAppFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FoundryDbContext>>();
            services.AddDbContext<FoundryDbContext>(options =>
                options.UseSqlite(_connection));

            services.RemoveAll<IHostedService>();
        });

        builder.UseEnvironment("Testing");
    }

    public async Task EnsureDatabaseCreatedAsync()
    {
        using IServiceScope scope = Services.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }
}
