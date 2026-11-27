using System.Text.Json;

using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared.Infrastructure.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Features.ProviderReactions.CreateIssueHandlerTests;

// Delivers IssueDetected through the real relay and processor, so the bridge's event is harvested
// into the outbox by the save the processor makes after the handler returns.
public sealed class WhenIssueDetectedIsDelivered : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenIssueDetectedIsDelivered()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task CommitsOneIssueEnteredTriageQueueOutboxRowForTheRepository()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IssueDetected detected = new(
            repositoryId,
            IssueNumber: 7,
            Title: "Blocked issue",
            Author: "octocat",
            Url: "https://github.com/owner/repo/issues/7",
            Labels: ["foundry"],
            IssueKindLabel: null,
            DetectedAt: DateTimeOffset.UtcNow);

        using (IServiceScope seedScope = _factory.Services.CreateScope())
        {
            DbContext seedContext = seedScope.ServiceProvider.GetRequiredService<DbContext>();
            seedContext.Set<OutboxMessage>().Add(OutboxMessage.Create(detected, DateTimeOffset.UtcNow.AddMinutes(-1)));
            await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        OutboxRelayService relay = _factory.Services.GetRequiredService<OutboxRelayService>();

        // Act
        await relay.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext dbContext = assertScope.ServiceProvider.GetRequiredService<DbContext>();

        TriageQueuedIssue issue = await dbContext.Set<Issue>()
            .OfType<TriageQueuedIssue>()
            .SingleAsync(i => i.MonitoredRepositoryId == repositoryId, TestContext.Current.CancellationToken);
        issue.IssueNumber.ShouldBe(7);

        string eventType = typeof(IssueEnteredTriageQueue).AssemblyQualifiedName!;
        List<OutboxMessage> nudgeRows = await dbContext.Set<OutboxMessage>()
            .Where(m => m.Type == eventType && m.ProcessedAt == null)
            .ToListAsync(TestContext.Current.CancellationToken);
        OutboxMessage row = nudgeRows.ShouldHaveSingleItem();
        JsonSerializer.Deserialize<IssueEnteredTriageQueue>(row.Payload, OutboxSerializerOptions.Default)
            .ShouldNotBeNull()
            .RepositoryId.ShouldBe(repositoryId);
    }
}
