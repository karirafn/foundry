using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features.WorkerConfig;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Events;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.WorkerConfig.UpdateWorkerImageConfigurationTests;

/// <summary>
/// Verifies that UpdateWorkerImageConfiguration.Handler enqueues WorkerImageConfigurationChanged
/// before SaveChangesAsync so the outbox interceptor harvests the event atomically.
/// </summary>
public sealed class OutboxHarvestHandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public OutboxHarvestHandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _serviceProvider = BuildServiceProvider(_connection);

        using IServiceScope scope = _serviceProvider.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        dbContext.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static ServiceProvider BuildServiceProvider(SqliteConnection connection)
    {
        ServiceCollection services = new();

        services.AddScoped<IntegrationEventCollector>();
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddScoped<IIntegrationEventDispatcher, OutboxIntegrationEventDispatcher>();

        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task WhenFlagsChange_WorkerImageConfigurationChangedRowPersistedAtomically()
    {
        // Arrange — seed settings with all flags off
        using (IServiceScope seedScope = _serviceProvider.CreateScope())
        {
            FoundryDbContext seedDb = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            GlobalSettings settings = GlobalSettings.Create();
            settings.UpdateWorkerImageConfiguration(new WorkerImageConfiguration(
                InstallDotnet: false,
                InstallAngular: false,
                InstallGlab: false,
                InstallGh: false,
                InstallChromium: false,
                InstallDocker: false));
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, integrationEventDispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert — success
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();

        // Assert — WorkerImageConfigurationChanged outbox row written atomically with change
        dbContext.ChangeTracker.Clear();
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(WorkerImageConfigurationChanged), StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenFlagsUnchanged_NoOutboxRowWritten()
    {
        // Arrange — seed settings with dotnet on, send same config
        using (IServiceScope seedScope = _serviceProvider.CreateScope())
        {
            FoundryDbContext seedDb = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            GlobalSettings settings = GlobalSettings.Create();
            settings.UpdateWorkerImageConfiguration(new WorkerImageConfiguration(
                InstallDotnet: true,
                InstallAngular: false,
                InstallGlab: false,
                InstallGh: false,
                InstallChromium: false,
                InstallDocker: false));
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, integrationEventDispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert — no outbox row because flags did not change
        dbContext.ChangeTracker.Clear();
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldBeEmpty();
    }
}
