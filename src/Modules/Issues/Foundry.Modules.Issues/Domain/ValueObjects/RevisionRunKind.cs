using Foundry.Modules.Issues.Contracts;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// The run kind of a revision, split by its revision trigger: the same kind whether the revision is queued or running.
/// </summary>
internal static class RevisionRunKind
{
    internal static RunKind Of(RevisionTrigger trigger) => trigger switch
    {
        RevisionTrigger.ReviewFeedback => RunKind.ReviewFix,
        RevisionTrigger.ReviewFindings => RunKind.FindingsFix,
        RevisionTrigger.FailedChecks => RunKind.ChecksFix,
        RevisionTrigger.RebaseConflict => RunKind.ConflictFix,
        RevisionTrigger.InputProvided => RunKind.InputResume,

        // A person rewrote the issue: the work resumes on what they wrote, as it does on their answers.
        RevisionTrigger.SpecChanged => RunKind.InputResume,
        _ => throw new InvalidOperationException($"Unknown revision trigger '{trigger.Discriminator}'."),
    };
}
