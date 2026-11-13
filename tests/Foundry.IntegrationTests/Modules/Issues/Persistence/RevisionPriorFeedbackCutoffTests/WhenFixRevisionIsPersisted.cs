using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Persistence.RevisionPriorFeedbackCutoffTests;

/// <summary>
/// The feedback cutoff a fix revision started from survives a database round trip, and a row without one (written
/// before the column existed) keeps the run-start rule (issue #925).
/// </summary>
public sealed class WhenFixRevisionIsPersisted : IAsyncDisposable
{
    private static readonly DateTimeOffset PriorCutoffAt = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStartedAt = new(2026, 6, 1, 11, 0, 0, TimeSpan.Zero);

    private readonly FoundryWebAppFactory _factory;

    public WhenFixRevisionIsPersisted()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task PriorFeedbackCutoffAt_RoundTrips()
    {
        // Arrange
        RevisionQueuedIssue revisionQueued = await SeedFixRevisionAsync();

        // Act
        RevisionQueuedIssue persisted = await LoadAsync(revisionQueued);

        // Assert
        persisted.PriorFeedbackCutoffAt.ShouldBe(PriorCutoffAt);
    }

    [Fact]
    public async Task WhenRowHasNoPriorFeedbackCutoff_CutoffIsRunStartedAt()
    {
        // Arrange
        RevisionQueuedIssue revisionQueued = await SeedFixRevisionAsync();
        await ClearPriorFeedbackCutoffAsync(revisionQueued);
        RevisionQueuedIssue legacy = await LoadAsync(revisionQueued);

        // Act
        RebasePendingIssue rebasePending = legacy
            .Claim(WorkerRunId.New(), RunStartedAt)
            .MarkRebasePending(RunStartedAt, DateTimeOffset.UtcNow);

        // Assert
        rebasePending.FeedbackCutoffAt.ShouldBe(RunStartedAt);
    }

    private async Task<RevisionQueuedIssue> SeedFixRevisionAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithFeedbackCutoffAt(PriorCutoffAt)
            .ChecksPending()
            .FixChecks([new FailingCheck("ci/build", "failure", null)], DateTimeOffset.UtcNow);

        dbContext.Set<Issue>().Add(revisionQueued);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return revisionQueued;
    }

    private async Task ClearPriorFeedbackCutoffAsync(RevisionQueuedIssue revisionQueued)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        await dbContext.Set<Issue>()
            .OfType<RevisionQueuedIssue>()
            .Where(i => i.Id == revisionQueued.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(i => i.PriorFeedbackCutoffAt, (DateTimeOffset?)null),
                TestContext.Current.CancellationToken);
    }

    private async Task<RevisionQueuedIssue> LoadAsync(RevisionQueuedIssue revisionQueued)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        Issue persisted = await dbContext.Set<Issue>()
            .SingleAsync(i => i.Id == revisionQueued.Id, TestContext.Current.CancellationToken);
        return persisted.ShouldBeOfType<RevisionQueuedIssue>();
    }
}
