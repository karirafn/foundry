namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// One step on an issue's lifecycle path, in snake_case wire tokens. <paramref name="Step"/> is one of the step
/// constants, in path order; <paramref name="Actor"/> is <see cref="ClaudeActor"/>, <see cref="CodeActor"/> or
/// <see cref="PersonActor"/>; <paramref name="Status"/> is one of the status constants. <paramref name="Phase"/> is set
/// only on the current step, and is <see langword="null"/> there too once the issue is done.
/// </summary>
public sealed record LifecycleStepInfo(string Step, string Actor, string Status, string? Phase)
{
    public const string DetectedStep = "detected";
    public const string TriageQueuedStep = "triage_queued";
    public const string TriageStep = "triage";
    public const string QueuedStep = "queued";
    public const string ImplementStep = "implement";
    public const string PublishStep = "publish";
    public const string RebaseStep = "rebase";
    public const string ChecksStep = "checks";
    public const string ReviewQueuedStep = "review_queued";
    public const string ReviewStep = "review";
    public const string YourReviewStep = "your_review";
    public const string MergeStep = "merge";
    public const string DoneStep = "done";

    public const string ClaudeActor = "claude";
    public const string CodeActor = "code";
    public const string PersonActor = "person";

    public const string DoneStatus = "done";
    public const string CurrentStatus = "current";
    public const string UpcomingStatus = "upcoming";
    public const string OffStatus = "off";
    public const string SkippedStatus = "skipped";

    public const string WaitingPhase = "waiting";
    public const string PreparingPhase = "preparing";
    public const string RunningPhase = "running";
    public const string NeedsYouPhase = "needs_you";
    public const string FailedPhase = "failed";
    public const string RetryingPhase = "retrying";
}
