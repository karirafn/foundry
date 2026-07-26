using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.ResumeDispatchTests;

/// <summary>
/// Verifies that ResumeDispatch.Handler enqueues DispatchResumed before SaveChangesAsync
/// so the outbox interceptor harvests the event atomically with the settings change.
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
    public async Task WhenSettingsExist_DispatchResumedRowPersistedAtomicallyWithSettingsChange()
    {
        // Arrange — seed paused GlobalSettings
        using (IServiceScope seedScope = _serviceProvider.CreateScope())
        {
            FoundryDbContext seedDb = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            GlobalSettings settings = GlobalSettings.Create();
            settings.PauseDispatch();
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        ResumeDispatch.Handler sut = new(dbContext, integrationEventDispatcher);

        // Act
        await sut.HandleAsync(new ResumeDispatch.Command(), TestContext.Current.CancellationToken);

        // Assert — settings persisted as resumed
        dbContext.ChangeTracker.Clear();
        GlobalSettings? stored = await dbContext.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.IsDispatchPaused.ShouldBeFalse();

        // Assert — DispatchResumed outbox row written atomically with settings change
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(DispatchResumed)));
    }
}
