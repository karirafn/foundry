using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.WorkerOutcomeResolverTests;

/// <summary>
/// A run that did not succeed — a non-zero exit, exit 0 with an <c>is_error</c> result line, or a vanished
/// container — fails whatever merge request its branch has, except a merged one.
/// </summary>
public sealed partial class ResolveAsync
{
    private const string RejectedRequestText = "API Error: 400 {\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\"}}";

    private const string RejectedRequestResultLine =
        """{"type":"result","subtype":"success","is_error":true,"num_turns":1,"result":"API Error: 400 {\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\"}}"}""";

    private const string CreditsExhaustedIsErrorResultLine =
        """{"type":"result","subtype":"success","is_error":true,"api_error_status":429,"num_turns":1,"result":"Credit balance is too low"}""";

    private static IPostExitProviderQueries OpenMrQueries()
        => new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.Open, PrUrl)));

    private static async Task<WorkerOutcome> ResolveWithOpenMrAsync(WorkerStatus? status, string? containerOutput)
    {
        WorkerOutcomeResolver sut = BuildResolverWithRealParser(OpenMrQueries());

        return await sut.ResolveAsync(CreateActiveRun(), status, containerOutput, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenOpenMrAndNonZeroExitWithIsErrorResultLine_ReturnsContinuableFailureCarryingResultText()
    {
        // Arrange
        string containerOutput = ContainerLog(RejectedRequestResultLine);

        // Act
        WorkerOutcome outcome = await ResolveWithOpenMrAsync(Exited(1), containerOutput);

        // Assert
        WorkerOutcome.ContinuableFailure failure = outcome.ShouldBeOfType<WorkerOutcome.ContinuableFailure>();
        failure.ShouldSatisfyAllConditions(
            () => failure.BranchName.Value.ShouldBe(DefaultBranchName),
            () => failure.FailureReason.ShouldBe(new FailureReason.NonZeroExit(1, RejectedRequestText)),
            () => failure.FailureReason.CategoryToken.ShouldBe(FailureReason.NonZeroExitToken),
            () => failure.FailureReason.Summary.ShouldBe($"Non-zero exit code: 1: {RejectedRequestText}"));
    }

    [Fact]
    public async Task WhenOpenMrAndExitZeroWithIsErrorResultLine_ReturnsContinuableFailureWithErrorResult()
    {
        // Arrange
        string containerOutput = ContainerLog(IsErrorResultLine);

        // Act
        WorkerOutcome outcome = await ResolveWithOpenMrAsync(Exited(0), containerOutput);

        // Assert
        WorkerOutcome.ContinuableFailure failure = outcome.ShouldBeOfType<WorkerOutcome.ContinuableFailure>();
        failure.FailureReason.ShouldBe(
            new FailureReason.ContainerError($"{WorkerOutcomeResolver.ErrorResultMessage}: {ErrorResultText}"));
    }

    [Fact]
    public async Task WhenOpenMrAndContainerGone_ReturnsContinuableFailureWithRemovedContainerError()
    {
        // Arrange

        // Act
        WorkerOutcome outcome = await ResolveWithOpenMrAsync(status: null, containerOutput: null);

        // Assert
        WorkerOutcome.ContinuableFailure failure = outcome.ShouldBeOfType<WorkerOutcome.ContinuableFailure>();
        failure.FailureReason.ShouldBe(new FailureReason.ContainerError(WorkerOutcomeResolver.RemovedOutsideFoundryMessage));
    }

    [Fact]
    public async Task WhenOpenMrAndExitZeroWithAuthInvalidIsErrorResultLine_ReturnsContinuableFailureWithAuthInvalid()
    {
        // Arrange
        string containerOutput = ContainerLog(AuthInvalidIsErrorResultLine);

        // Act
        WorkerOutcome outcome = await ResolveWithOpenMrAsync(Exited(0), containerOutput);

        // Assert
        WorkerOutcome.ContinuableFailure failure = outcome.ShouldBeOfType<WorkerOutcome.ContinuableFailure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.AuthInvalid>();
    }

    [Fact]
    public async Task WhenOpenMrAndExitZeroWithOverloadedIsErrorResultLine_ReturnsContinuableFailureWithTransientApiError()
    {
        // Arrange
        string containerOutput = ContainerLog(OverloadedIsErrorResultLine);

        // Act
        WorkerOutcome outcome = await ResolveWithOpenMrAsync(Exited(0), containerOutput);

        // Assert
        WorkerOutcome.ContinuableFailure failure = outcome.ShouldBeOfType<WorkerOutcome.ContinuableFailure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.TransientApiError>();
    }

    [Fact]
    public async Task WhenOpenMrAndExitZeroWithCreditsExhaustedIsErrorResultLine_ReturnsContinuableFailureWithCreditsExhausted()
    {
        // Arrange
        string containerOutput = ContainerLog(CreditsExhaustedIsErrorResultLine);

        // Act
        WorkerOutcome outcome = await ResolveWithOpenMrAsync(Exited(0), containerOutput);

        // Assert
        WorkerOutcome.ContinuableFailure failure = outcome.ShouldBeOfType<WorkerOutcome.ContinuableFailure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.CreditsExhausted>();
    }

    [Fact]
    public async Task WhenOpenMrAndExitZeroWithIsErrorAndNeedsInputVerdict_ReturnsContinuableFailure()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildResolverWithRealParser(OpenMrQueries());
        RunVerdict verdict = RunVerdict.NeedsInput([new RunVerdictQuestion("Which database?", "Two are configured.", "Postgres.")]);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateActiveRun(),
            Exited(0),
            ContainerLog(IsErrorResultLine),
            verdict,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.ContinuableFailure>();
    }

    [Fact]
    public async Task WhenOpenMrAndExitZeroWithSuccessResultLine_ReturnsReview()
    {
        // Arrange
        string containerOutput = ContainerLog(SuccessResultLine);

        // Act
        WorkerOutcome outcome = await ResolveWithOpenMrAsync(Exited(0), containerOutput);

        // Assert
        WorkerOutcome.Review review = outcome.ShouldBeOfType<WorkerOutcome.Review>();
        review.PullRequestUrl.Value.ShouldBe(PrUrl);
    }

    [Fact]
    public async Task WhenMergedMrAndNonZeroExit_ReturnsCompleted()
    {
        // Arrange
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.Merged, PrUrl)));
        WorkerOutcomeResolver sut = BuildResolverWithRealParser(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateActiveRun(),
            Exited(1),
            ContainerLog(RejectedRequestResultLine),
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Completed>();
    }

    [Fact]
    public async Task WhenNoMrAndExitZeroWithIsErrorAndCommits_ReturnsContinuableFailureWithoutRepolling()
    {
        // Arrange — a repoll would find the open merge request and return Review
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.Open, PrUrl)),
            mrResults: [Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null))]);
        WorkerOutcomeResolver sut = BuildResolverWithRealParser(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateActiveRun(),
            Exited(0),
            ContainerLog(IsErrorResultLine),
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.ContinuableFailure failure = outcome.ShouldBeOfType<WorkerOutcome.ContinuableFailure>();
        failure.FailureReason.ShouldBe(
            new FailureReason.ContainerError($"{WorkerOutcomeResolver.ErrorResultMessage}: {ErrorResultText}"));
    }

    [Fact]
    public async Task WhenNonZeroExitWithLongMultilineSecretBearingIsErrorText_DetailIsRedactedSingleLineAndTruncated()
    {
        // Arrange
        string longText = "API Error: 400 ghp_secrettoken\\nsecond " + new string('x', WorkerOutcomeResolver.MaxErrorResultTextLength * 2);
        string containerOutput = ContainerLog($$"""{"type":"result","is_error":true,"result":"{{longText}}"}""");
        WorkerOutcomeResolver sut = BuildResolverWithRealParser(NoMrQueries(hasCommits: false));

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateActiveRun(),
            Exited(1),
            containerOutput,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        FailureReason.NonZeroExit nonZeroExit = failure.FailureReason.ShouldBeOfType<FailureReason.NonZeroExit>();
        string detail = nonZeroExit.Detail.ShouldNotBeNull();
        detail.ShouldSatisfyAllConditions(
            () => detail.Length.ShouldBe(WorkerOutcomeResolver.MaxErrorResultTextLength),
            () => detail.ShouldStartWith("API Error: 400 *** second x"),
            () => detail.ShouldNotContain("ghp_"),
            () => detail.ShouldNotContain('\n'));
    }

    [Fact]
    public async Task WhenNonZeroExitWithIsErrorFalseResultLine_ReturnsNonZeroExitWithoutDetail()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildResolverWithRealParser(NoMrQueries(hasCommits: false));

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateActiveRun(),
            Exited(1),
            ContainerLog(SuccessResultLine),
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBe(new FailureReason.NonZeroExit(1));
    }

    [Fact]
    public async Task WhenNonZeroExitWithIsErrorResultLineWithoutText_ReturnsNonZeroExitWithoutDetail()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildResolverWithRealParser(NoMrQueries(hasCommits: false));

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateActiveRun(),
            Exited(1),
            ContainerLog("""{"type":"result","subtype":"error_during_execution","is_error":true,"num_turns":0}"""),
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBe(new FailureReason.NonZeroExit(1));
    }
}
