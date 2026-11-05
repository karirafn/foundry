using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Features.RebaseGate;

// Handlers have no HTTP endpoint — dispatched through the integration-event pipeline.
// These tests resolve handlers from DI and call them directly against the real wired SQLite context.
public sealed class WhenRebaseResolved : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    public WhenRebaseResolved()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task<RebasePendingIssue> SeedRebasePendingIssueAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        RebasePendingIssue rebasePending = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .WithIssueNumber(1)
            .WithTitle("Rebase pending issue")
            .WithBranchName("feat/1-test")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/1")
            .WithFeedbackCutoffAt(DateTimeOffset.UtcNow.AddDays(1))
            .RebasePending();

        dbContext.Set<Issue>().Add(rebasePending);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return rebasePending;
    }

    private async Task HandleAsync<TEvent>(TEvent @event)
        where TEvent : IIntegrationEvent
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IIntegrationEventHandler<TEvent> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TEvent>>();
        await handler.HandleAsync(@event, TestContext.Current.CancellationToken);
    }

    private async Task<Issue?> LoadAsync(RebasePendingIssue seeded)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        return await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == seeded.Id, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SkippedOutcome_IsPersistedThroughChecksPendingIntoReview()
    {
        // Arrange
        RebasePendingIssue rebasePending = await SeedRebasePendingIssueAsync();

        // Act
        await HandleAsync(new RebaseResolved(RepositoryId, 1, "skipped", RebaseSkipReasons.Timeout));
        await HandleAsync(new ChecksVerdictResolved(RepositoryId, 1, "passed", []));

        // Assert
        ReviewIssue review = (await LoadAsync(rebasePending)).ShouldBeOfType<ReviewIssue>();
        review.RebaseOutcome.ShouldBe(new RebaseOutcome.Skipped(RebaseSkipReasons.Timeout));
        review.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>();
    }

    [Fact]
    public async Task PullRequestClosedWhileRebasePending_FailsTheIssue()
    {
        // Arrange
        RebasePendingIssue rebasePending = await SeedRebasePendingIssueAsync();

        // Act
        await HandleAsync(new ProviderPullRequestClosed(RepositoryId, 1));

        // Assert
        (await LoadAsync(rebasePending)).ShouldBeOfType<ContinuableFailedIssue>();
    }
}
