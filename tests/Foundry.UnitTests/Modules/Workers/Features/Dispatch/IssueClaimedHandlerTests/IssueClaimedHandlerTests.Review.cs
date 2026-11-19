using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.IssueClaimedHandlerTests;

public sealed partial class HandleAsync
{
    private const string ReviewedBranch = "feat/42-test-issue";
    private const string ReviewedPullRequest = "https://github.com/owner/repo/pull/7";
    private const string ReviewedCommitSha = "abcdefabcdefabcdefabcdefabcdefabcdefabcd";
    private const string ReviewPatchId = "0123456789012345678901234567890123456789";

    private const string ReviewIssueBody = """
        Adds retries.

        ## Acceptance Criteria

        - [ ] Retries three times
        - [x] Logs each retry

        ## Notes

        - Not a criterion
        """;

    private static readonly ReviewPacketFacts ReviewPacket = new(
        [
            new ChangedPath(ChangeStatus.Modified, "src/Retry.cs"),
            new ChangedPath(ChangeStatus.Added, "tests/RetryTests.cs"),
        ],
        PatchId.From(ReviewPatchId));

    private static DispatchContext.Review ReviewContext(string reviewedCommit = ReviewedCommitSha) =>
        new(ReviewedBranch, ReviewedPullRequest, reviewedCommit);

    private static IssueClaimed BuildReviewEvent(DispatchContext.Review? context = null) =>
        BuildEvent(context: context ?? ReviewContext(), withoutBranch: true);

    private static WorkerRoleCatalog CatalogWithRestricted(WorkerRole role)
    {
        WorkerRoleCatalog implementOnly = StubWorkerRoleCatalogStore.Catalog();
        WorkerRoleDefinition restricted = new(
            role,
            WorkerCapability.Restricted,
            new RolePlugin(role.Name, "A restricted role.", [], []),
            "You are reviewing issue #{issueNumber}.\n\n{issueContent}",
            "Review issue #{issueNumber}.",
            VerdictSchema: null);

        return implementOnly with
        {
            Content = implementOnly.Content with { Roles = [.. implementOnly.Content.Roles, restricted] },
        };
    }

    private IssueClaimedHandler BuildReviewHandler(
        StubWorkerOrchestrator? orchestrator = null,
        FakePublisher? publisher = null,
        string issueBody = ReviewIssueBody)
    {
        return BuildHandler(
            orchestrator: orchestrator,
            publisher: publisher ?? new FakePublisher(reviewPacket: ReviewPacket),
            roleCatalog: new StubWorkerRoleCatalogStore(CatalogWithRestricted(WorkerRole.Review)),
            issueContentQueries: new StubIssueContentQueries(
                Result<IssueContent>.Ok(new IssueContent("Add retries", issueBody, []))));
    }

    [Fact]
    public async Task WhenTheContextIsReview_PreparesTheReviewedBranchAndCommitAndNoRunBranch()
    {
        // Arrange
        FakePublisher publisher = new(reviewPacket: ReviewPacket);
        IssueClaimedHandler sut = BuildReviewHandler(publisher: publisher);

        // Act
        await sut.HandleAsync(BuildReviewEvent(), TestContext.Current.CancellationToken);

        // Assert
        PrepareRequest request = publisher.Requests.ShouldHaveSingleItem();
        request.ShouldSatisfyAllConditions(
            () => request.Review.ShouldBe(new ReviewTarget(BranchName.From(ReviewedBranch), CommitSha.From(ReviewedCommitSha))),
            () => request.RunBranch.ShouldBeNull());
    }

    [Fact]
    public async Task WhenTheContextIsReview_TheRunRecordsTheFactsComputedFromThePacketAndTheIssueBody()
    {
        // Arrange
        IssueClaimedHandler sut = BuildReviewHandler();

        // Act
        await sut.HandleAsync(BuildReviewEvent(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        ActiveRun run = (await _dbContext.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken)).ShouldBeOfType<ActiveRun>();
        SuppliedReviewFacts facts = run.SuppliedReviewFacts.ShouldNotBeNull();
        facts.ShouldSatisfyAllConditions(
            () => facts.PullRequestUrl.ShouldBe(PullRequestUrl.From(ReviewedPullRequest)),
            () => facts.ReviewedCommit.ShouldBe(CommitSha.From(ReviewedCommitSha)),
            () => facts.PatchId.ShouldBe(PatchId.From(ReviewPatchId)),
            () => facts.ClassifiedPaths.Select(path => (path.Path.Path, path.Class)).ShouldBe(
                [("src/Retry.cs", ChangedFileClass.Production), ("tests/RetryTests.cs", ChangedFileClass.Test)]),
            () => facts.Criteria.Select(criterion => (criterion.Id, criterion.Text)).ShouldBe(
                [("AC1", "Retries three times"), ("AC2", "Logs each retry")]));
    }

    [Fact]
    public async Task WhenTheReviewedIssueHasNoAcceptanceCriteriaHeading_TheRunRecordsNoCriteria()
    {
        // Arrange
        IssueClaimedHandler sut = BuildReviewHandler(issueBody: "Adds retries.");

        // Act
        await sut.HandleAsync(BuildReviewEvent(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        WorkerRun run = await _dbContext.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
        run.SuppliedReviewFacts.ShouldNotBeNull().Criteria.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheReviewedChangeHasNoChangedPaths_TheRunRecordsAnEmptyTableNoFlagAndNoPatchId()
    {
        // Arrange
        IssueClaimedHandler sut = BuildReviewHandler(publisher: new FakePublisher(reviewPacket: new ReviewPacketFacts([], patchId: null)));

        // Act
        await sut.HandleAsync(BuildReviewEvent(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        SuppliedReviewFacts facts = (await _dbContext.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken))
            .SuppliedReviewFacts.ShouldNotBeNull();
        facts.ShouldSatisfyAllConditions(
            () => facts.ClassifiedPaths.ShouldBeEmpty(),
            () => facts.ProductionChangedWithoutTests.ShouldBeFalse(),
            () => facts.PatchId.ShouldBeNull());
    }

    [Fact]
    public async Task WhenTheContextIsReview_TheSystemPromptCarriesTheReviewFacts()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        IssueClaimedHandler sut = BuildReviewHandler(orchestrator: orchestrator);

        // Act
        await sut.HandleAsync(BuildReviewEvent(), TestContext.Current.CancellationToken);

        // Assert
        string systemPrompt = orchestrator.LastSpec.ShouldNotBeNull().EnvironmentVariables["SYSTEM_PROMPT"];
        systemPrompt.ShouldSatisfyAllConditions(
            () => systemPrompt.ShouldContain("You are reviewing issue #42."),
            () => systemPrompt.ShouldContain("## Review facts"),
            () => systemPrompt.ShouldContain($"Reviewed commit: {ReviewedCommitSha}"),
            () => systemPrompt.ShouldContain("AC1: Retries three times"),
            () => systemPrompt.ShouldContain("| M | production | src/Retry.cs |"));
    }

    [Fact]
    public async Task WhenTheContextIsReview_TheContainerIsARestrictedReviewRunOnTheReviewedCommit()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        IssueClaimedHandler sut = BuildReviewHandler(orchestrator: orchestrator);
        WorkerRunId runId = WorkerRunId.New();

        // Act
        await sut.HandleAsync(
            BuildEvent(workerRunId: runId, context: ReviewContext(), withoutBranch: true),
            TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec spec = orchestrator.LastSpec.ShouldNotBeNull();
        spec.ShouldSatisfyAllConditions(
            () => spec.EnvironmentVariables["WORKER_ROLE"].ShouldBe("review"),
            () => spec.EnvironmentVariables["WORKER_CAPABILITY"].ShouldBe("restricted"),
            () => spec.EnvironmentVariables["REVIEW_COMMIT"].ShouldBe(ReviewedCommitSha),
            () => spec.EnvironmentVariables.ShouldNotContainKey("BRANCH_NAME"),
            () => spec.EnvironmentVariables.ShouldNotContainKey("GIT_PAT"),
            () => spec.EnvironmentVariables.ShouldNotContainKey("GH_TOKEN"),
            () => spec.EnvironmentVariables.ShouldNotContainKey("GITLAB_TOKEN"),
            () => spec.VolumeMounts.ShouldContain(
                new VolumeMount(WorkerVolumeNames.HandOffVolumeName(runId), WorkerVolumeNames.HandOffContainerPath)));
    }

    [Fact]
    public async Task WhenPreparingTheReviewedCommitFails_TheRunFailsWithItsReasonAndNoWorkerStarts()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        FailureReason reason = new FailureReason.WorkerBootstrapFailed(
            "FOUNDRY_BOOTSTRAP_FAILED stage=fetch detail=REVIEW_COMMIT is not on the branch");
        IssueClaimedHandler sut = BuildReviewHandler(
            orchestrator: orchestrator,
            publisher: new FakePublisher(new PrepareOutcome.Failed(reason)));

        // Act
        await sut.HandleAsync(BuildReviewEvent(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        WorkerRun run = await _dbContext.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<FailedRun>().Reason.ShouldBe(reason);
        orchestrator.LastSpec.ShouldBeNull();
    }

    [Fact]
    public async Task WhenPrepareReturnsNoReviewPacket_TheRunFailsAndNoWorkerStarts()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        PrepareOutcome withoutPacket = new PrepareOutcome.Prepared(new RunBase(FakePublisher.DefaultBranchHead, RunBranchHead: null));
        IssueClaimedHandler sut = BuildReviewHandler(orchestrator: orchestrator, publisher: new FakePublisher(withoutPacket));

        // Act
        await sut.HandleAsync(BuildReviewEvent(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        (await _dbContext.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken)).ShouldBeOfType<FailedRun>();
        orchestrator.LastSpec.ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheReviewedCommitIsNotACommitSha_TheRunFailsBeforeAnythingIsPrepared()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        FakePublisher publisher = new(reviewPacket: ReviewPacket);
        IssueClaimedHandler sut = BuildReviewHandler(orchestrator: orchestrator, publisher: publisher);

        // Act
        await sut.HandleAsync(BuildReviewEvent(ReviewContext(reviewedCommit: "main")), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        (await _dbContext.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken)).ShouldBeOfType<FailedRun>();
        publisher.Requests.ShouldBeEmpty();
        orchestrator.LastSpec.ShouldBeNull();
    }

    public static TheoryData<DispatchContext, bool> NonReviewContexts => new()
    {
        { new DispatchContext.Fresh("feat/42-test-issue"), false },
        { new DispatchContext.Revision("feat/42-test-issue", "https://github.com/owner/repo/pull/1", new RevisionTrigger.InputProvided()), false },
        { new DispatchContext.Continuation("feat/42-test-issue", "timed out"), false },
        { new DispatchContext.Triage(), true },
    };

    [Theory]
    [MemberData(nameof(NonReviewContexts))]
    public async Task WhenTheContextIsNotReview_NothingIsPreparedForReviewAndTheContainerHasNoReviewCommit(
        DispatchContext context,
        bool restricted)
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        FakePublisher publisher = new();
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            publisher: publisher,
            roleCatalog: restricted
                ? new StubWorkerRoleCatalogStore(CatalogWithRestricted(WorkerRole.Triage))
                : StubWorkerRoleCatalogStore.With());

        // Act
        await sut.HandleAsync(BuildEvent(context: context, withoutBranch: restricted), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        publisher.Requests.ShouldHaveSingleItem().Review.ShouldBeNull();
        orchestrator.LastSpec.ShouldNotBeNull().EnvironmentVariables.ShouldNotContainKey("REVIEW_COMMIT");
        (await _dbContext.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken)).SuppliedReviewFacts.ShouldBeNull();
    }
}
