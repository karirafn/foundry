using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Persistence.IssueStepTransitionLogTests;

public sealed class WhenIssueIsDeleted : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenIssueIsDeleted()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task AnUntrackedIssuesRowsAreDeletedWithIt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue untracked = await AddAsync(new IssueBuilder().WithMonitoredRepositoryId(repositoryId).WithIssueNumber(1).Detected());
        DetectedIssue kept = await AddAsync(new IssueBuilder().WithMonitoredRepositoryId(repositoryId).WithIssueNumber(2).Detected());

        // Act
        await DeliverAsync(new ProviderIssueUntracked(repositoryId, IssueNumber: 1));

        // Assert
        (await ReadLogAsync(untracked.Id), await ReadLogAsync(kept.Id))
            .ShouldBe((0, 1));
    }

    [Fact]
    public async Task ARemovedRepositorysDeletedIssuesLoseTheirRowsAndCompletedIssuesKeepThem()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = await AddAsync(new IssueBuilder().WithMonitoredRepositoryId(repositoryId).WithIssueNumber(1).FreshQueued());
        CompletedIssue completed = await AddAsync(new IssueBuilder().WithMonitoredRepositoryId(repositoryId).WithIssueNumber(2).Completed());

        // Act
        await DeliverAsync(new MonitoredRepositoryRemoved(repositoryId, DateTimeOffset.UtcNow));

        // Assert
        (await ReadLogAsync(queued.Id), await ReadLogAsync(completed.Id))
            .ShouldBe((0, 1));
    }

    private async Task<TIssue> AddAsync<TIssue>(TIssue issue)
        where TIssue : Issue
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
        db.Add(issue);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return issue;
    }

    // Every module's handler runs, as the relay delivers the event to each of them.
    private async Task DeliverAsync<TEvent>(TEvent @event)
        where TEvent : IIntegrationEvent
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        foreach (IIntegrationEventHandler<TEvent> handler in
            scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>())
        {
            await handler.HandleAsync(@event, TestContext.Current.CancellationToken);
        }
    }

    private async Task<int> ReadLogAsync(IssueId issueId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IssueStepTransitionQueries queries = scope.ServiceProvider.GetRequiredService<IssueStepTransitionQueries>();
        return (await queries.GetForIssueAsync(issueId, TestContext.Current.CancellationToken)).Count;
    }
}
