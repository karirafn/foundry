using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts.Queries;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.IssueLifecyclePathTests;

/// <summary>
/// Every combination of the three repository settings, <c>HasPersonFeedback</c> and a current step before, at and
/// after <c>checks</c> gives each step the status the path rules state.
/// </summary>
public sealed class Build
{
    private static readonly IssueLifecycleStep[] Order =
    [
        IssueLifecycleStep.Detected,
        IssueLifecycleStep.TriageQueued,
        IssueLifecycleStep.Triage,
        IssueLifecycleStep.Queued,
        IssueLifecycleStep.Implement,
        IssueLifecycleStep.Publish,
        IssueLifecycleStep.Rebase,
        IssueLifecycleStep.Checks,
        IssueLifecycleStep.ReviewQueued,
        IssueLifecycleStep.Review,
        IssueLifecycleStep.YourReview,
        IssueLifecycleStep.Merge,
        IssueLifecycleStep.Done,
    ];

    private static readonly IssueLifecycleStep[] CurrentSteps =
    [
        IssueLifecycleStep.Implement,
        IssueLifecycleStep.Checks,
        IssueLifecycleStep.Review,
        IssueLifecycleStep.YourReview,
        IssueLifecycleStep.Merge,
        IssueLifecycleStep.Done,
    ];

    public static TheoryData<bool, bool, bool, bool, IssueLifecycleStep> Combinations()
    {
        TheoryData<bool, bool, bool, bool, IssueLifecycleStep> data = [];
        bool[] values = [false, true];
        foreach (bool rebase in values)
        {
            foreach (bool review in values)
            {
                foreach (bool merge in values)
                {
                    foreach (bool personFeedback in values)
                    {
                        foreach (IssueLifecycleStep current in CurrentSteps)
                        {
                            data.Add(rebase, review, merge, personFeedback, current);
                        }
                    }
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void WhenBuilt_EachStepHasTheStatusTheRulesGive(
        bool rebaseAfterRun,
        bool reviewPullRequests,
        bool mergeWhenChecksPass,
        bool hasPersonFeedback,
        IssueLifecycleStep current)
    {
        // Arrange
        RepositoryLifecycleSettings settings = new(rebaseAfterRun, reviewPullRequests, mergeWhenChecksPass);
        IReadOnlyList<LifecycleStepStatus> expected = Order
            .Select(step => ExpectedStatus(step, current, settings, hasPersonFeedback))
            .ToList();

        // Act
        IReadOnlyList<LifecyclePathStep> path = IssueLifecyclePath.Build(
            current,
            LifecyclePhase.Running,
            hasPersonFeedback,
            settings);

        // Assert
        path.Select(s => (s.Step, s.Status)).ShouldBe(Order.Zip(expected));
    }

    [Fact]
    public void WhenAPersonsFeedbackDroveARevisionWithMergeOn_MergeIsSkippedAndYourReviewIsUpcoming()
    {
        // Arrange
        RepositoryLifecycleSettings settings = new(RebaseAfterRun: true, ReviewPullRequests: true, MergeWhenChecksPass: true);

        // Act
        IReadOnlyList<LifecyclePathStep> path = IssueLifecyclePath.Build(
            IssueLifecycleStep.Checks,
            LifecyclePhase.Running,
            hasPersonFeedback: true,
            settings);

        // Assert
        (StatusOf(path, IssueLifecycleStep.YourReview), StatusOf(path, IssueLifecycleStep.Merge))
            .ShouldBe((LifecycleStepStatus.Upcoming, LifecycleStepStatus.Skipped));
    }

    [Fact]
    public void WhenReviewIsOff_TheReviewQueueAndReviewAreOff()
    {
        // Arrange
        RepositoryLifecycleSettings settings = new(RebaseAfterRun: true, ReviewPullRequests: false, MergeWhenChecksPass: false);

        // Act
        IReadOnlyList<LifecyclePathStep> path = IssueLifecyclePath.Build(
            IssueLifecycleStep.Implement,
            LifecyclePhase.Running,
            hasPersonFeedback: false,
            settings);

        // Assert
        (StatusOf(path, IssueLifecycleStep.ReviewQueued), StatusOf(path, IssueLifecycleStep.Review))
            .ShouldBe((LifecycleStepStatus.Off, LifecycleStepStatus.Off));
    }

    [Fact]
    public void WhenARevisionIsQueued_StepsAfterQueuedAreUpcomingAndOnlyEarlierStepsAreDone()
    {
        // Arrange
        RepositoryLifecycleSettings settings = new(RebaseAfterRun: true, ReviewPullRequests: true, MergeWhenChecksPass: false);

        // Act
        IReadOnlyList<LifecyclePathStep> path = IssueLifecyclePath.Build(
            IssueLifecycleStep.Queued,
            LifecyclePhase.Waiting,
            hasPersonFeedback: false,
            settings);

        // Assert
        path.Select(s => s.Status).ShouldBe(
        [
            LifecycleStepStatus.Done,
            LifecycleStepStatus.Done,
            LifecycleStepStatus.Done,
            LifecycleStepStatus.Current,
            LifecycleStepStatus.Upcoming,
            LifecycleStepStatus.Upcoming,
            LifecycleStepStatus.Upcoming,
            LifecycleStepStatus.Upcoming,
            LifecycleStepStatus.Upcoming,
            LifecycleStepStatus.Upcoming,
            LifecycleStepStatus.Upcoming,
            LifecycleStepStatus.Off,
            LifecycleStepStatus.Upcoming,
        ]);
    }

    [Fact]
    public void WhenBuilt_OnlyTheCurrentStepCarriesThePhase()
    {
        // Arrange
        RepositoryLifecycleSettings settings = new(RebaseAfterRun: true, ReviewPullRequests: true, MergeWhenChecksPass: true);

        // Act
        IReadOnlyList<LifecyclePathStep> path = IssueLifecyclePath.Build(
            IssueLifecycleStep.Rebase,
            LifecyclePhase.Running,
            hasPersonFeedback: false,
            settings);

        // Assert
        path.Where(s => s.Phase is not null).Select(s => (s.Step, s.Phase))
            .ShouldBe([(IssueLifecycleStep.Rebase, (LifecyclePhase?)LifecyclePhase.Running)]);
    }

    private static LifecycleStepStatus StatusOf(IReadOnlyList<LifecyclePathStep> path, IssueLifecycleStep step) =>
        path.Single(s => s.Step == step).Status;

    // The rules restated from the path's definition: the current step is current; a setting that turns a step off
    // makes it off; a step this issue's path does not take is skipped; otherwise done before the current step and
    // upcoming after it.
    private static LifecycleStepStatus ExpectedStatus(
        IssueLifecycleStep step,
        IssueLifecycleStep current,
        RepositoryLifecycleSettings settings,
        bool hasPersonFeedback)
    {
        if (step == current)
        {
            return LifecycleStepStatus.Current;
        }

        bool off = step switch
        {
            IssueLifecycleStep.Rebase => !settings.RebaseAfterRun,
            IssueLifecycleStep.ReviewQueued or IssueLifecycleStep.Review => !settings.ReviewPullRequests,
            IssueLifecycleStep.Merge => !settings.MergeWhenChecksPass,
            _ => false,
        };
        if (off)
        {
            return LifecycleStepStatus.Off;
        }

        bool skipped = step switch
        {
            IssueLifecycleStep.Merge => hasPersonFeedback || current == IssueLifecycleStep.YourReview,
            IssueLifecycleStep.YourReview => settings.MergeWhenChecksPass && !hasPersonFeedback,
            _ => false,
        };
        if (skipped)
        {
            return LifecycleStepStatus.Skipped;
        }

        return Array.IndexOf(Order, step) < Array.IndexOf(Order, current)
            ? LifecycleStepStatus.Done
            : LifecycleStepStatus.Upcoming;
    }
}
