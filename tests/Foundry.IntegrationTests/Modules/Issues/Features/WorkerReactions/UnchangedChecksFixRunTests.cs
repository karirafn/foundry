using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Features.WorkerReactions;

// A checks-fix run that pushes no commit spends the rest of the fix budget (issue #1140), and the spent budget
// survives the round trip through the checks_fix_attempts column.
public sealed class UnchangedChecksFixRunTests : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public UnchangedChecksFixRunTests()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenFailedChecksRunCompletesWithoutPublishing_PersistsAnExhaustedFixBudget()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        RevisionInProgressIssue fixRun = new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.New())
            .WithWorkerRunId(workerRunId)
            .WithBranchName("feat/1-test")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/1")
            .ChecksPending()
            .FixChecks(
                [new FailingCheck("CI / build", "failure", "https://github.com/owner/repo/actions/runs/1")],
                DateTimeOffset.UtcNow)
            .Claim(workerRunId, DateTimeOffset.UtcNow);
        fixRun.Trigger.ShouldBeOfType<RevisionTrigger.FailedChecks>();

        using (IServiceScope seedScope = _factory.Services.CreateScope())
        {
            DbContext seedDb = seedScope.ServiceProvider.GetRequiredService<DbContext>();
            seedDb.Set<Issue>().Add(fixRun);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WorkerRunCompleted @event = new(
            WorkerRunId: workerRunId,
            IssueId: fixRun.Id.Value,
            BranchName: null,
            PullRequestUrl: null,
            MergeState: WorkerRunMergeState.None,
            RunStartedAt: DateTimeOffset.UtcNow);

        // Act
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IIntegrationEventHandler<WorkerRunCompleted> handler =
                scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<WorkerRunCompleted>>();
            await handler.HandleAsync(@event, TestContext.Current.CancellationToken);
        }

        // Assert
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext dbContext = assertScope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? persisted = await dbContext.Set<Issue>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == fixRun.Id, TestContext.Current.CancellationToken);

        persisted.ShouldBeOfType<RebasePendingIssue>()
            .ChecksFixAttempts.ShouldBe(ChecksPendingIssue.ChecksMaxFixPushes);
    }
}
