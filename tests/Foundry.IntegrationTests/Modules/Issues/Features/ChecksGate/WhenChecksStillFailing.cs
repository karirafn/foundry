using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Features.ChecksGate;

// Handler has no HTTP endpoint — dispatched through the integration-event pipeline.
// These tests exercise the real DbContext (SQLite, no Docker) against the live handler wiring.
//
// The three "at the cap" tests prove the ChecksOutcome.StillFailing JSON column round-trips
// its failing-check list intact when ChecksFixAttempts has reached ChecksMaxFixPushes.
// The "under cap" tests prove the handler dispatches a FixChecks revision instead.
public sealed class WhenChecksStillFailing : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    public WhenChecksStillFailing()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    // Seeds a ChecksPendingIssue at the cap (ChecksFixAttempts = 2).
    // At the cap a still_failing verdict transitions to ReviewIssue rather than dispatching another fix.
    private async Task<ChecksPendingIssue> SeedChecksPendingIssueAtCapAsync(int issueNumber = 1)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle("Still failing checks issue")
            .WithBranchName("feat/1-test")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/1")
            .WithFeedbackCutoffAt(DateTimeOffset.UtcNow.AddDays(1))
            .ChecksPendingAtCap();

        dbContext.Set<Issue>().Add(checksPending);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return checksPending;
    }

    // Seeds a ChecksPendingIssue with ChecksFixAttempts = 0 (under the cap).
    // Under the cap a still_failing verdict dispatches a FixChecks revision.
    private async Task<ChecksPendingIssue> SeedChecksPendingIssueUnderCapAsync(int issueNumber = 10)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle("Under-cap checks issue")
            .WithBranchName("feat/1-test")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/1")
            .WithFeedbackCutoffAt(DateTimeOffset.UtcNow.AddDays(1))
            .ChecksPending();

        dbContext.Set<Issue>().Add(checksPending);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return checksPending;
    }

    [Fact]
    public async Task WhenChecksVerdictResolvedWithStillFailing_PersistedReviewHasStillFailingOutcome()
    {
        // Arrange
        ChecksPendingIssue checksPending = await SeedChecksPendingIssueAtCapAsync();

        IReadOnlyList<FailingCheck> failingChecks =
        [
            new FailingCheck("CI / build", "failure", "https://github.com/owner/repo/actions/runs/1"),
            new FailingCheck("CI / test", "failure", null),
        ];

        ChecksVerdictResolved @event = new(
            RepositoryId: RepositoryId,
            IssueNumber: checksPending.IssueNumber,
            Outcome: "still_failing",
            FailingChecks: failingChecks);

        // Act
        using IServiceScope scope = _factory.Services.CreateScope();
        IIntegrationEventHandler<ChecksVerdictResolved> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<ChecksVerdictResolved>>();
        await handler.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — read back from the real DB to confirm the JSON column round-trips
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext dbContext = assertScope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? persisted = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == checksPending.Id, TestContext.Current.CancellationToken);

        ReviewIssue review = persisted.ShouldBeOfType<ReviewIssue>();
        ChecksOutcome.StillFailing stillFailing = review.ChecksOutcome.ShouldBeOfType<ChecksOutcome.StillFailing>();
        stillFailing.FailingChecks.Count.ShouldBe(2);
        stillFailing.FailingChecks.ShouldSatisfyAllConditions(
            _ => stillFailing.FailingChecks[0].Name.ShouldBe("CI / build"),
            _ => stillFailing.FailingChecks[0].Conclusion.ShouldBe("failure"),
            _ => stillFailing.FailingChecks[0].DetailsUrl.ShouldBe("https://github.com/owner/repo/actions/runs/1"),
            _ => stillFailing.FailingChecks[1].Name.ShouldBe("CI / test"),
            _ => stillFailing.FailingChecks[1].DetailsUrl.ShouldBeNull());
    }

    [Fact]
    public async Task WhenChecksVerdictResolvedWithStillFailing_DetailDtoSurfacesOutcomeAndFailingChecks()
    {
        // Arrange
        ChecksPendingIssue checksPending = await SeedChecksPendingIssueAtCapAsync(issueNumber: 2);

        IReadOnlyList<FailingCheck> failingChecks =
        [
            new FailingCheck("CI / lint", "failure", "https://github.com/owner/repo/actions/runs/2"),
        ];

        ChecksVerdictResolved @event = new(
            RepositoryId: RepositoryId,
            IssueNumber: checksPending.IssueNumber,
            Outcome: "still_failing",
            FailingChecks: failingChecks);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IIntegrationEventHandler<ChecksVerdictResolved> handler =
                scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<ChecksVerdictResolved>>();
            await handler.HandleAsync(@event, TestContext.Current.CancellationToken);
        }

        // Act — read through the real IIssueQueries to assert the DTO projection
        using IServiceScope queryScope = _factory.Services.CreateScope();
        IIssueQueries queries = queryScope.ServiceProvider.GetRequiredService<IIssueQueries>();
        Result<IssueDetail> result = await queries.GetIssueDetailAsync(
            checksPending.Id,
            TestContext.Current.CancellationToken);

        // Assert
        IssueDetail detail = result.ShouldBeOfType<Result<IssueDetail>.Success>().Value;
        detail.State.ShouldBe("review");
        IssueStateDetails stateDetails = detail.StateDetails.ShouldNotBeNull();
        stateDetails.ChecksOutcome.ShouldBe("still_failing");
        IReadOnlyList<FailingCheck> returnedChecks = stateDetails.FailingChecks.ShouldNotBeNull();
        returnedChecks.Count.ShouldBe(1);
        returnedChecks[0].ShouldSatisfyAllConditions(
            () => returnedChecks[0].Name.ShouldBe("CI / lint"),
            () => returnedChecks[0].Conclusion.ShouldBe("failure"),
            () => returnedChecks[0].DetailsUrl.ShouldBe("https://github.com/owner/repo/actions/runs/2"));
    }

    [Fact]
    public async Task WhenChecksVerdictResolvedWithStillFailing_SummaryDtoSurfacesChecksOutcome()
    {
        // Arrange
        ChecksPendingIssue checksPending = await SeedChecksPendingIssueAtCapAsync(issueNumber: 3);

        ChecksVerdictResolved @event = new(
            RepositoryId: RepositoryId,
            IssueNumber: checksPending.IssueNumber,
            Outcome: "still_failing",
            FailingChecks: [new FailingCheck("CI / build", "failure", null)]);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IIntegrationEventHandler<ChecksVerdictResolved> handler =
                scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<ChecksVerdictResolved>>();
            await handler.HandleAsync(@event, TestContext.Current.CancellationToken);
        }

        // Act — read through the real IIssueQueries to assert the IssueSummary projection
        using IServiceScope queryScope = _factory.Services.CreateScope();
        IIssueQueries queries = queryScope.ServiceProvider.GetRequiredService<IIssueQueries>();
        IssueSummary? summary = await queries.GetIssueSummaryAsync(
            checksPending.Id,
            TestContext.Current.CancellationToken);

        // Assert
        IssueSummary result = summary.ShouldNotBeNull();
        result.State.ShouldBe("review");
        result.ChecksOutcome.ShouldBe("still_failing");
    }

    [Fact]
    public async Task WhenChecksVerdictResolvedWithStillFailing_UnderCap_PersistedIssueIsRevisionQueued()
    {
        // Arrange — ChecksFixAttempts = 0, still below ChecksMaxFixPushes = 2.
        IReadOnlyList<FailingCheck> failingChecks =
        [
            new FailingCheck("CI / build", "failure", "https://github.com/owner/repo/actions/runs/10"),
            new FailingCheck("CI / test", "failure", null),
        ];

        ChecksPendingIssue checksPending = await SeedChecksPendingIssueUnderCapAsync(issueNumber: 10);

        ChecksVerdictResolved @event = new(
            RepositoryId: RepositoryId,
            IssueNumber: checksPending.IssueNumber,
            Outcome: "still_failing",
            FailingChecks: failingChecks);

        // Act
        using IServiceScope scope = _factory.Services.CreateScope();
        IIntegrationEventHandler<ChecksVerdictResolved> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<ChecksVerdictResolved>>();
        await handler.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — the issue becomes a RevisionQueuedIssue with a FailedChecks trigger.
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext dbContext = assertScope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? persisted = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == checksPending.Id, TestContext.Current.CancellationToken);

        RevisionQueuedIssue revisionQueued = persisted.ShouldBeOfType<RevisionQueuedIssue>();
        revisionQueued.ChecksFixAttempts.ShouldBe(1);
        RevisionTrigger.FailedChecks trigger = revisionQueued.Trigger.ShouldBeOfType<RevisionTrigger.FailedChecks>();
        trigger.FailingChecks.ShouldSatisfyAllConditions(
            _ => trigger.FailingChecks.Count.ShouldBe(2),
            _ => trigger.FailingChecks[0].Name.ShouldBe("CI / build"),
            _ => trigger.FailingChecks[0].Conclusion.ShouldBe("failure"),
            _ => trigger.FailingChecks[0].DetailsUrl.ShouldBe("https://github.com/owner/repo/actions/runs/10"),
            _ => trigger.FailingChecks[1].Name.ShouldBe("CI / test"),
            _ => trigger.FailingChecks[1].DetailsUrl.ShouldBeNull());
    }

    [Fact]
    public async Task WhenChecksVerdictResolvedWithStillFailing_OneFixAttemptRemaining_DispatchesAnotherFix()
    {
        // Arrange — ChecksFixAttempts = 1 (one attempt made, one remaining before cap).
        IReadOnlyList<FailingCheck> failingChecks =
        [
            new FailingCheck("CI / lint", "failure", "https://github.com/owner/repo/actions/runs/11"),
        ];

        ChecksPendingIssue checksPending = await SeedChecksPendingIssueWithOneAttemptAsync(issueNumber: 11);

        ChecksVerdictResolved @event = new(
            RepositoryId: RepositoryId,
            IssueNumber: checksPending.IssueNumber,
            Outcome: "still_failing",
            FailingChecks: failingChecks);

        // Act
        using IServiceScope scope = _factory.Services.CreateScope();
        IIntegrationEventHandler<ChecksVerdictResolved> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<ChecksVerdictResolved>>();
        await handler.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — a second fix revision is dispatched; ChecksFixAttempts reaches the cap (2).
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext dbContext = assertScope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? persisted = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == checksPending.Id, TestContext.Current.CancellationToken);

        RevisionQueuedIssue revisionQueued = persisted.ShouldBeOfType<RevisionQueuedIssue>();
        revisionQueued.ChecksFixAttempts.ShouldBe(2);
        revisionQueued.Trigger.ShouldBeOfType<RevisionTrigger.FailedChecks>();
    }

    // Seeds a ChecksPendingIssue with ChecksFixAttempts = 1 (one fix attempt already made).
    private async Task<ChecksPendingIssue> SeedChecksPendingIssueWithOneAttemptAsync(int issueNumber)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle("One-attempt checks issue")
            .WithBranchName("feat/1-test")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/1")
            .WithFeedbackCutoffAt(DateTimeOffset.UtcNow.AddDays(1))
            .ChecksPendingFromFixChecks();

        dbContext.Set<Issue>().Add(checksPending);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return checksPending;
    }
}
