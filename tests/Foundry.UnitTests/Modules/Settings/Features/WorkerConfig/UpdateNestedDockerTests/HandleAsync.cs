using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features.WorkerConfig;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

using Foundry.UnitTests.Modules.Settings.Features;

namespace Foundry.UnitTests.Modules.Settings.Features.WorkerConfig.UpdateNestedDockerTests;

/// <summary>
/// The nested-Docker switch governs run privileges, not image contents (ADR 0110): saving it persists the switch
/// and writes no outbox row, so no rebuild starts. The outbox interceptor is wired so an enqueued event would show.
/// </summary>
public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public HandleAsync()
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

    private async Task SeedSettingsAsync()
    {
        using IServiceScope seedScope = _serviceProvider.CreateScope();
        FoundryDbContext seedDb = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        seedDb.Set<GlobalSettings>().Add(GlobalSettings.Create());
        await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Result<GlobalSettingsSummary>> HandleAsyncInNewScope(bool enabled)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        UpdateNestedDocker.Handler sut = new(dbContext, new StubWebhookIngressProbe());

        return await sut.HandleAsync(new UpdateNestedDocker.Command(enabled), TestContext.Current.CancellationToken);
    }

    private async Task<GlobalSettings> ReadStoredSettingsAsync()
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        GlobalSettings? stored = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        return stored.ShouldNotBeNull();
    }

    private async Task<List<OutboxMessage>> ReadOutboxAsync()
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        return await dbContext.Set<OutboxMessage>().ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenTurnedOn_PersistsTheSwitch()
    {
        // Arrange
        await SeedSettingsAsync();

        // Act
        await HandleAsyncInNewScope(enabled: true);

        // Assert
        GlobalSettings stored = await ReadStoredSettingsAsync();
        stored.NestedDockerEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTurnedOn_ReturnsSummaryWithTheSwitchOn()
    {
        // Arrange
        await SeedSettingsAsync();

        // Act
        Result<GlobalSettingsSummary> result = await HandleAsyncInNewScope(enabled: true);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>().Value.NestedDockerEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTurnedOn_WritesNoOutboxRow()
    {
        // Arrange
        await SeedSettingsAsync();

        // Act
        await HandleAsyncInNewScope(enabled: true);

        // Assert
        List<OutboxMessage> messages = await ReadOutboxAsync();
        messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTurnedOn_LeavesTheWorkerImageConfigurationAndBuildStateUnchanged()
    {
        // Arrange
        await SeedSettingsAsync();

        // Act
        await HandleAsyncInNewScope(enabled: true);

        // Assert
        GlobalSettings stored = await ReadStoredSettingsAsync();
        stored.ShouldSatisfyAllConditions(
            () => stored.WorkerImageConfiguration.ShouldBe(WorkerImageConfiguration.Default),
            () => stored.ImageBuildState.ShouldBeOfType<ImageBuildState.Idle>());
    }

    [Fact]
    public async Task WhenTurnedOff_PersistsTheSwitch()
    {
        // Arrange
        await SeedSettingsAsync();
        await HandleAsyncInNewScope(enabled: true);

        // Act
        await HandleAsyncInNewScope(enabled: false);

        // Assert
        GlobalSettings stored = await ReadStoredSettingsAsync();
        stored.NestedDockerEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenSettingsNotFound_ReturnsNotFoundError()
    {
        // Act
        Result<GlobalSettingsSummary> result = await HandleAsyncInNewScope(enabled: true);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>().Error.Code.ShouldBe(SettingsErrors.NotFoundCode);
    }
}
