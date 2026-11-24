using Foundry.Modules.Issues.Contracts;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>The wire token of each <see cref="IssueLifecycleStep"/>: the summary's step and the stored log column.</summary>
internal static class IssueLifecycleStepTokens
{
    public static string ToToken(IssueLifecycleStep step) =>
        step switch
        {
            IssueLifecycleStep.Detected => LifecycleStepInfo.DetectedStep,
            IssueLifecycleStep.TriageQueued => LifecycleStepInfo.TriageQueuedStep,
            IssueLifecycleStep.Triage => LifecycleStepInfo.TriageStep,
            IssueLifecycleStep.Queued => LifecycleStepInfo.QueuedStep,
            IssueLifecycleStep.Implement => LifecycleStepInfo.ImplementStep,
            IssueLifecycleStep.Publish => LifecycleStepInfo.PublishStep,
            IssueLifecycleStep.Rebase => LifecycleStepInfo.RebaseStep,
            IssueLifecycleStep.Checks => LifecycleStepInfo.ChecksStep,
            IssueLifecycleStep.ReviewQueued => LifecycleStepInfo.ReviewQueuedStep,
            IssueLifecycleStep.Review => LifecycleStepInfo.ReviewStep,
            IssueLifecycleStep.YourReview => LifecycleStepInfo.YourReviewStep,
            IssueLifecycleStep.Merge => LifecycleStepInfo.MergeStep,
            IssueLifecycleStep.Done => LifecycleStepInfo.DoneStep,
            _ => throw new InvalidOperationException($"Unknown lifecycle step: {step}"),
        };

    /// <summary>The step <paramref name="token"/> names, or <see langword="null"/> for an unknown token.</summary>
    public static IssueLifecycleStep? FromToken(string token)
    {
        foreach (IssueLifecycleStep step in Enum.GetValues<IssueLifecycleStep>())
        {
            if (ToToken(step) == token)
            {
                return step;
            }
        }

        return null;
    }
}
