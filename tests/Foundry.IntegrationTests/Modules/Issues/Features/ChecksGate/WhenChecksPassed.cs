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
// These tests resolve handlers from DI and call them directly against the real wired SQLite context.
//
// Docker is NOT available in this sandbox — compile-verified only; executes in CI.
public sealed class WhenChecksPassed : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    public WhenChecksPassed()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task<ChecksPendingIssue> SeedChecksPendingIssueAsync(int issueNumber = 1)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle("Checks pending issue")
            .WithBranchName("feat/1-test")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/1")
            .WithFeedbackCutoffAt(DateTimeOffset.UtcNow.AddDays(1))
            .ChecksPending();

        dbContext.Set<Issue>().Add(checksPending);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return checksPending;
    }

    [Fact]
    public async Task WhenChecksVerdictResolvedWithPassed_TransitionsIssueToReviewWithPassedOutcome()
    {
        // Arrange
        ChecksPendingIssue checksPending = await SeedChecksPendingIssueAsync();
        ChecksVerdictResolved @event = new(
            RepositoryId: RepositoryId,
            IssueNumber: checksPending.IssueNumber,
            Outcome: "passed",
            FailingChecks: []);

        // Act
        using IServiceScope scope = _factory.Services.CreateScope();
        IIntegrationEventHandler<ChecksVerdictResolved> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<ChecksVerdictResolved>>();
        await handler.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext dbContext = assertScope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? persisted = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == checksPending.Id, TestContext.Current.CancellationToken);

        ReviewIssue review = persisted.ShouldBeOfType<ReviewIssue>();
        review.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>();
    }

    [Fact]
    public async Task WhenChecksVerdictResolvedWithPassed_ChecksOutcomeDiscriminatorIsPassedOnDetail()
    {
        // Arrange
        ChecksPendingIssue checksPending = await SeedChecksPendingIssueAsync(issueNumber: 2);
        ChecksVerdictResolved @event = new(
            RepositoryId: RepositoryId,
            IssueNumber: checksPending.IssueNumber,
            Outcome: "passed",
            FailingChecks: []);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IIntegrationEventHandler<ChecksVerdictResolved> handler =
                scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<ChecksVerdictResolved>>();
            await handler.HandleAsync(@event, TestContext.Current.CancellationToken);
        }

        // Act — fetch through the real IIssueQueries to assert the DTO projection
        using IServiceScope queryScope = _factory.Services.CreateScope();
        IIssueQueries queries = queryScope.ServiceProvider.GetRequiredService<IIssueQueries>();
        Result<IssueDetail> result = await queries.GetIssueDetailAsync(
            checksPending.Id,
            TestContext.Current.CancellationToken);

        // Assert
        IssueDetail detail = result.ShouldBeOfType<Result<IssueDetail>.Success>().Value;
        detail.State.ShouldBe("review");
        IssueStateDetails stateDetails = detail.StateDetails.ShouldNotBeNull();
        stateDetails.ShouldSatisfyAllConditions(
            () => stateDetails.ChecksOutcome.ShouldBe("passed"),
            () => stateDetails.FailingChecks.ShouldBeNull());
    }
}
