using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Persistence.IssueStepTransitionLogTests;

public sealed class WhenIssueTransitions : IAsyncDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly FoundryWebAppFactory _factory;

    public WhenIssueTransitions()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task EachCommittedTransitionAppendsItsRowInEntryOrder()
    {
        // Arrange
        FreshQueuedIssue queued = new IssueBuilder().WithTransitionedAt(Start).FreshQueued();
        await AddAsync(queued);

        // Act
        InProgressIssue inProgress = queued.Claim(WorkerRunId.New(), Start.AddMinutes(1));
        await TransitionAsync(queued, inProgress);
        RebasePendingIssue rebasePending = inProgress.MarkRebasePending(
            "feat/1-test",
            "https://github.com/owner/repo/pull/1",
            Start,
            Start.AddMinutes(2));
        await TransitionAsync(inProgress, rebasePending);
        ChecksPendingIssue checksPending = rebasePending.MarkChecksPending(new RebaseOutcome.UpToDate(), Start.AddMinutes(3));
        await TransitionAsync(rebasePending, checksPending);
        RevisionQueuedIssue revisionQueued = checksPending.FixChecks(
            [new FailingCheck("ci/build", "failure", null)],
            Start.AddMinutes(4));
        await TransitionAsync(checksPending, revisionQueued);

        // Assert
        IReadOnlyList<IssueStepTransition> log = await ReadLogAsync(queued.Id);
        log.Select(t => (t.State, t.Round, t.Cause)).ShouldBe(
        [
            ("queued", 1, null),
            ("in_progress", 1, null),
            ("rebase_pending", 1, "after_run"),
            ("checks_pending", 1, "after_run"),
            ("revision_queued", 2, "failed_checks"),
        ]);
    }

    [Fact]
    public async Task TheRowRecordsTheStatesStepRoundEntryTimeAndCause()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().WithTransitionedAt(Start).ChecksPending();
        await AddAsync(checksPending);
        RevisionQueuedIssue revisionQueued = checksPending.FixChecks(
            [new FailingCheck("ci/build", "failure", null)],
            Start.AddMinutes(5));

        // Act
        await TransitionAsync(checksPending, revisionQueued);

        // Assert
        IssueStepTransition row = (await ReadLogAsync(checksPending.Id))[^1];
        (row.IssueId, row.State, row.LifecycleStep, row.Round, row.EnteredAt, row.Cause).ShouldBe(
            (checksPending.Id, "revision_queued", IssueLifecycleStep.Queued, 2, Start.AddMinutes(5), "failed_checks"));
    }

    [Fact]
    public async Task ANewlyAddedIssueGetsItsFirstRow()
    {
        // Arrange
        DetectedIssue detected = new IssueBuilder().WithDetectedAt(Start).Detected();

        // Act
        await AddAsync(detected);

        // Assert
        IReadOnlyList<IssueStepTransition> log = await ReadLogAsync(detected.Id);
        log.Select(t => (t.State, t.LifecycleStep, t.Round, t.EnteredAt, t.Cause))
            .ShouldBe([("detected", IssueLifecycleStep.Detected, 1, Start, (string?)null)]);
    }

    [Fact]
    public async Task ATransitionRolledBackWithItsOuterTransactionWritesNoRow()
    {
        // Arrange
        FreshQueuedIssue queued = new IssueBuilder().WithTransitionedAt(Start).FreshQueued();
        await AddAsync(queued);

        // Act
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
            IDomainEventDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();
            FreshQueuedIssue tracked = (FreshQueuedIssue)await db.Set<Issue>()
                .SingleAsync(i => i.Id == queued.Id, TestContext.Current.CancellationToken);
            await using IDbContextTransaction transaction =
                await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await db.TransitionAsync(
                tracked,
                tracked.Claim(WorkerRunId.New(), Start.AddMinutes(1)),
                dispatcher,
                TestContext.Current.CancellationToken);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        IReadOnlyList<IssueStepTransition> log = await ReadLogAsync(queued.Id);
        log.Select(t => t.State).ShouldBe(["queued"]);
    }

    private async Task AddAsync(Issue issue)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
        db.Add(issue);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task TransitionAsync<TFrom, TTo>(TFrom old, TTo next)
        where TFrom : Issue
        where TTo : Issue
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
        IDomainEventDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();
        db.Attach(old);
        await db.TransitionAsync(old, next, dispatcher, TestContext.Current.CancellationToken);
    }

    private async Task<IReadOnlyList<IssueStepTransition>> ReadLogAsync(IssueId issueId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IssueStepTransitionQueries queries = scope.ServiceProvider.GetRequiredService<IssueStepTransitionQueries>();
        return await queries.GetForIssueAsync(issueId, TestContext.Current.CancellationToken);
    }
}
