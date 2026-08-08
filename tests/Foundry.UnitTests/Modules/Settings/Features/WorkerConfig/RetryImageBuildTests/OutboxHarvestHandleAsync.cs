using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Features.WorkerConfig;
using Foundry.Modules.Settings.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.WorkerConfig.RetryImageBuildTests;

/// <summary>
/// Verifies that RetryImageBuild.Handler enqueues WorkerImageConfigurationChanged and
/// the outbox interceptor harvests the event atomically (via SaveChangesAsync added in step 9a).
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
    public async Task WhenStatusIsFailed_WorkerImageConfigurationChangedRowPersistedAtomically()
    {
        // Arrange — seed settings with Failed image build state
        using (IServiceScope seedScope = _serviceProvider.CreateScope())
        {
            FoundryDbContext seedDb = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            GlobalSettings settings = GlobalSettings.Create();
            settings.FailImageBuild("previous error", nextRetryAt: null, attempt: 0);
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        RetryImageBuild.Handler sut = new(dbContext, integrationEventDispatcher);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            new RetryImageBuild.Command(),
            TestContext.Current.CancellationToken);

        // Assert — success
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();

        // Assert — WorkerImageConfigurationChanged outbox row written to database
        dbContext.ChangeTracker.Clear();
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(WorkerImageConfigurationChanged)));
    }

    [Fact]
    public async Task WhenStatusIsNotFailed_NoOutboxRowWritten()
    {
        // Arrange — seed settings with Idle image build state (not Failed)
        using (IServiceScope seedScope = _serviceProvider.CreateScope())
        {
            FoundryDbContext seedDb = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            GlobalSettings settings = GlobalSettings.Create();
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        RetryImageBuild.Handler sut = new(dbContext, integrationEventDispatcher);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            new RetryImageBuild.Command(),
            TestContext.Current.CancellationToken);

        // Assert — failure (invalid state)
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>();

        // Assert — no outbox row because dispatch was not reached
        dbContext.ChangeTracker.Clear();
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldBeEmpty();
    }
}
