using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
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
    private readonly Action<IServiceCollection>? _serviceOverrides;

    public FoundryWebAppFactory(Action<IServiceCollection>? serviceOverrides = null)
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _serviceOverrides = serviceOverrides;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FoundryDbContext>>();
            services.AddDbContext<FoundryDbContext>(options =>
                options.UseSqlite(_connection));

            services.RemoveAll<IHostedService>();

            // Use ephemeral data protection keys so tests do not depend on
            // a persistent key store and encryption works within a single test run.
            services.AddDataProtection()
                .UseEphemeralDataProtectionProvider();

            _serviceOverrides?.Invoke(services);

            using ServiceProvider sp = services.BuildServiceProvider();
            using IServiceScope scope = sp.CreateScope();
            FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            dbContext.Database.EnsureCreated();
        });

        builder.UseEnvironment("Testing");
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }
}
