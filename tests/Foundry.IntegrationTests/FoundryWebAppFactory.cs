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
    private const string InMemoryDataSource = ":memory:";

    private readonly SqliteConnection _connection;
    private readonly Action<IServiceCollection>? _serviceOverrides;

    // Set only by the composed-host mode, which keeps every hosted service running.
    private readonly FileInfo? _databaseFile;

    private DirectoryInfo? _persistentKeysDirectory;
    private string? _legacyApplicationName;

    public FoundryWebAppFactory()
        : this(OpenMigratedDatabase(InMemoryDataSource), serviceOverrides: null, databaseFile: null)
    {
    }

    private FoundryWebAppFactory(SqliteConnection connection, Action<IServiceCollection>? serviceOverrides, FileInfo? databaseFile)
    {
        _connection = connection;
        _serviceOverrides = serviceOverrides;
        _databaseFile = databaseFile;

        // The test server takes the request scheme from this address, so requests arrive as HTTPS and
        // UseHttpsRedirection passes them on without looking up an HTTPS port.
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    public SqliteConnection Connection => _connection;

    private bool KeepsHostedServices => _databaseFile is not null;

    public static FoundryWebAppFactory WithPersistentDataProtection(
        DirectoryInfo keysDirectory,
        string? legacyApplicationName = null) =>
        new()
        {
            _persistentKeysDirectory = keysDirectory,
            _legacyApplicationName = legacyApplicationName,
        };

    public static FoundryWebAppFactory WithOverrides(Action<IServiceCollection> serviceOverrides) =>
        new(OpenMigratedDatabase(InMemoryDataSource), serviceOverrides, databaseFile: null);

    /// <summary>
    /// A host whose database starts empty, so its startup in <c>Program.cs</c> applies every migration.
    /// Every other host starts from a copy of <see cref="MigratedDatabaseTemplate"/>.
    /// </summary>
    public static FoundryWebAppFactory WithEmptyDatabase() =>
        new(OpenDatabase(InMemoryDataSource), serviceOverrides: null, databaseFile: null);

    /// <summary>
    /// The composed host: every hosted service runs at its production cadence against a fresh database
    /// file, and <paramref name="boundaryFakes"/> replaces only the external boundaries.
    /// </summary>
    public static FoundryWebAppFactory WithHostedServices(Action<IServiceCollection> boundaryFakes)
    {
        // Concurrent hosted services cannot share one SqliteConnection, so this mode runs on a database
        // file that every DbContext opens its own connection to, as production does.
        FileInfo databaseFile = new(Path.Combine(Path.GetTempPath(), $"foundry-smoke-{Guid.NewGuid():N}.db"));
        SqliteConnection connection = OpenMigratedDatabase(databaseFile.FullName);

        // In the default rollback journal a reader's shared lock makes a concurrent UPDATE ... RETURNING fail with
        // "database is locked" when it commits, and Microsoft.Data.Sqlite does not retry that commit. With every hosted
        // service polling, any read can race a write, so the test failed by chance. WAL lets readers and the one writer
        // run concurrently; the mode is stored in the file, so every connection the host opens uses it. It is set after
        // the template copy, which overwrites the file's header and with it the journal mode.
        using (SqliteCommand journalMode = connection.CreateCommand())
        {
            journalMode.CommandText = "PRAGMA journal_mode=WAL;";
            journalMode.ExecuteNonQuery();
        }

        return new FoundryWebAppFactory(connection, boundaryFakes, databaseFile);
    }

    private static SqliteConnection OpenMigratedDatabase(string dataSource)
    {
        SqliteConnection connection = OpenDatabase(dataSource);
        MigratedDatabaseTemplate.CopyInto(connection);
        return connection;
    }

    private static SqliteConnection OpenDatabase(string dataSource)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = dataSource }.ToString());
        connection.Open();
        return connection;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FoundryDbContext>>();
            services.AddDbContext<FoundryDbContext>((sp, options) =>
            {
                if (KeepsHostedServices)
                {
                    options.UseSqlite(_connection.ConnectionString);
                }
                else
                {
                    options.UseSqlite(_connection);
                }

                options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
            });

            if (!KeepsHostedServices)
            {
                services.RemoveAll<IHostedService>();

                // Re-register OutboxRelayService as a plain singleton so integration tests can resolve
                // it and call TickForTest to drive one relay pass deterministically, without starting
                // the background timer loop (R1 from the outbox design).
                services.AddSingleton<OutboxRelayService>();
            }

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
        if (_databaseFile is null)
        {
            await _connection.DisposeAsync();
            await base.DisposeAsync();
            return;
        }

        // The host stops first so no hosted service still writes to the file when it is deleted.
        await base.DisposeAsync();
        await _connection.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _databaseFile.Delete();
    }
}
