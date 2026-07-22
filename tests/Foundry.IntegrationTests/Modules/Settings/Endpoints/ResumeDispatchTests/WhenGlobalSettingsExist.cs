using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.ResumeDispatchTests;

public sealed class WhenGlobalSettingsExist : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenGlobalSettingsExist()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedPausedSettingsAsync()
    {
        // SettingsSeeder is a hosted service stripped in tests — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        settings.PauseDispatch();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsOkWithDispatchResumed()
    {
        // Arrange
        await SeedPausedSettingsAsync();

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri("/api/settings/dispatch/resume", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.IsDispatchPaused.ShouldBeFalse();
    }

    /// <summary>
    /// Pre-ship integration test for the transactional outbox.
    ///
    /// Proves the crash-between-commit-and-delivery acceptance criterion:
    /// a DispatchResumed integration event written to outbox_messages before delivery is picked up
    /// and fully processed on the next relay pass — even if the process crashed between the commit
    /// and the in-process dispatch attempt.
    ///
    /// The outbox row is seeded directly because the ResumeDispatch handler still uses the
    /// pre-migration dispatch-after-save pattern (step 9 migrates it). Seeding directly is the
    /// correct approach per integration-tests.md: "Use DbContext directly only when no endpoint can
    /// produce the required state."
    /// </summary>
    [Fact]
    public async Task WhenOutboxMessageExists_RelayDeliversEventAndStampsProcessedAt()
    {
        // Arrange — write a DispatchResumed outbox row as if the interceptor had persisted it.
        using IServiceScope seedScope = _factory.Services.CreateScope();
        DbContext seedContext = seedScope.ServiceProvider.GetRequiredService<DbContext>();

        OutboxMessage message = OutboxMessage.Create(new DispatchResumed(), DateTimeOffset.UtcNow);
        seedContext.Set<OutboxMessage>().Add(message);
        await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        Guid messageId = message.Id;

        // Assert — exactly one row exists and is NOT yet delivered (crash-window proof).
        using IServiceScope preTickScope = _factory.Services.CreateScope();
        DbContext preTickContext = preTickScope.ServiceProvider.GetRequiredService<DbContext>();

        List<OutboxMessage> preTick = await preTickContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);

        preTick.Count.ShouldBe(1);
        preTick[0].ProcessedAt.ShouldBeNull("message must be undelivered before the relay runs");

        // Act — drive exactly one relay tick via the TickForTest seam.
        // OutboxRelayService is registered as a plain singleton (not IHostedService) in the test
        // factory so the background timer does not run, but the service is still resolvable here.
        OutboxRelayService relay = _factory.Services.GetRequiredService<OutboxRelayService>();
        await relay.TickForTest(TestContext.Current.CancellationToken);

        // Assert — the outbox row is now published (ProcessedAt stamped by the relay).
        using IServiceScope postTickScope = _factory.Services.CreateScope();
        DbContext postTickContext = postTickScope.ServiceProvider.GetRequiredService<DbContext>();

        OutboxMessage? published = await postTickContext
            .Set<OutboxMessage>()
            .FirstOrDefaultAsync(m => m.Id == messageId, TestContext.Current.CancellationToken);

        published.ShouldNotBeNull();
        published.ProcessedAt.ShouldNotBeNull(
            "relay must have stamped ProcessedAt after delivering the event");

        // Assert — processed_events rows exist for each registered DispatchResumed handler,
        // proving inbox deduplication records were committed (at-least-once + idempotent delivery).
        // Two handlers are registered: DispatchResumedHandler (Issues) and
        // DispatchResumedBroadcastHandler (Workers).
        List<ProcessedEvent> processedEvents = await postTickContext
            .Set<ProcessedEvent>()
            .Where(p => p.EventId == messageId)
            .ToListAsync(TestContext.Current.CancellationToken);

        processedEvents.Count.ShouldBe(2, "both DispatchResumed handlers must record a processed_events row");
    }
}
