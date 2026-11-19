using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.WorkerOutcomeResolverTests;

public sealed partial class ResolveAsync
{
    private static readonly CommitSha PublishedHead = CommitSha.From("3333333333333333333333333333333333333333");

    private static PublishingRun CreatePublishedRun()
    {
        PublishingRun run = CreateActiveRun().BeginPublishing(
            new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow),
            containerOutput: null,
            RunVerdict.Done(),
            new RunPullRequestText("Add feature", "Body"));
        run.RecordPublished(new PublishResult.Published(PublishedHead, 1, ["feat: add feature"], [], []), DateTimeOffset.UtcNow);
        return run;
    }

    [Fact]
    public async Task WhenRunPublishedAHead_LooksUpTheMergeRequestByThatHead()
    {
        // Arrange
        ScriptedProviderQueries queries = new(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.Open, PrUrl)));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        await sut.ResolveAsync(CreatePublishedRun(), status: Exited(0), containerOutput: null, TestContext.Current.CancellationToken);

        // Assert
        queries.ReceivedPublishedHeads.ShouldBe([PublishedHead]);
    }

    [Fact]
    public async Task WhenRepollingForTheMergeRequest_LooksItUpByThePublishedHead()
    {
        // Arrange
        ScriptedProviderQueries queries = new(
            commitsResult: Result<bool>.Ok(true),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        await sut.ResolveAsync(CreatePublishedRun(), status: Exited(0), containerOutput: null, TestContext.Current.CancellationToken);

        // Assert
        queries.ShouldSatisfyAllConditions(
            () => queries.ReceivedPublishedHeads.Count.ShouldBeGreaterThan(1),
            () => queries.ReceivedPublishedHeads.ShouldAllBe(head => head == PublishedHead));
    }

    [Fact]
    public async Task TryResolveExistingPullRequest_LooksUpTheMergeRequestByThePublishedHead()
    {
        // Arrange
        ScriptedProviderQueries queries = new(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.Open, PrUrl)));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        await sut.TryResolveExistingPullRequestAsync(
            CreatePublishedRun(),
            containerOutput: null,
            summary: null,
            TestContext.Current.CancellationToken);

        // Assert
        queries.ReceivedPublishedHeads.ShouldBe([PublishedHead]);
    }

    [Fact]
    public async Task WhenRunPublishedNothing_LooksUpTheMergeRequestWithoutAHead()
    {
        // Arrange
        ScriptedProviderQueries queries = new(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.Open, PrUrl)));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        await sut.ResolveAsync(CreateActiveRun(), status: Exited(0), containerOutput: null, TestContext.Current.CancellationToken);

        // Assert
        queries.ReceivedPublishedHeads.ShouldBe([null]);
    }
}
