using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Shared.Infrastructure.Outbox;
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
    // Matches the server-side JSON options registered in Program.cs (camelCase property names,
    // string enum serialization). Tests that deserialize response bodies with enum properties must
    // pass these options to ReadFromJsonAsync so string enum tokens round-trip correctly.
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private const string LegacyApplicationNameSetting = "DataProtection:LegacyApplicationName";

    private readonly SqliteConnection _connection;
    private readonly Action<IServiceCollection>? _serviceOverrides;
    private DirectoryInfo? _persistentKeysDirectory;
    private string? _legacyApplicationName;

    public FoundryWebAppFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    private FoundryWebAppFactory(Action<IServiceCollection> serviceOverrides)
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _serviceOverrides = serviceOverrides;
    }

    public SqliteConnection Connection => _connection;

    public static FoundryWebAppFactory WithPersistentDataProtection(
        DirectoryInfo keysDirectory,
        string? legacyApplicationName = null) =>
        new()
        {
            _persistentKeysDirectory = keysDirectory,
            _legacyApplicationName = legacyApplicationName,
        };

    public static FoundryWebAppFactory WithOverrides(Action<IServiceCollection> serviceOverrides) =>
        new(serviceOverrides);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FoundryDbContext>>();
            services.AddDbContext<FoundryDbContext>((sp, options) =>
            {
                options.UseSqlite(_connection);
                options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
            });

            services.RemoveAll<IHostedService>();

            // Re-register OutboxRelayService as a plain singleton so integration tests can resolve
            // it and call TickForTest to drive one relay pass deterministically, without starting
            // the background timer loop (R1 from the outbox design).
            services.AddSingleton<OutboxRelayService>();

            // Ephemeral keys by default so tests do not depend on a persistent key store;
            // a persistent key ring lets a test seed ciphertext before the host boots.
            IDataProtectionBuilder dataProtection = services.AddDataProtection();
            if (_persistentKeysDirectory is null)
            {
                dataProtection.UseEphemeralDataProtectionProvider();
            }
            else
            {
                dataProtection.PersistKeysToFileSystem(_persistentKeysDirectory);
            }

            _serviceOverrides?.Invoke(services);
        });

        if (_legacyApplicationName is not null)
        {
            builder.UseSetting(LegacyApplicationNameSetting, _legacyApplicationName);
        }

        builder.UseEnvironment("Testing");
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }
}
