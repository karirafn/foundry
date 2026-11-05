using System.Diagnostics;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// Closed union recording how the post-run rebase of the worker branch onto the default branch resolved
/// before the issue left the rebase-pending state (ADR 0087). Persisted on checks-pending and review issues.
/// </summary>
public abstract record RebaseOutcome
{
    private RebaseOutcome()
    {
    }

    /// <summary>
    /// A stable string token for each variant, suitable for persistence and wire encoding.
    /// </summary>
    public string Discriminator => this switch
    {
        UpToDate => UpToDateDiscriminator,
        Rebased => RebasedDiscriminator,
        Disabled => DisabledDiscriminator,
        Skipped => SkippedDiscriminator,
        Conflict => ConflictDiscriminator,
        _ => throw new UnreachableException($"Unknown {nameof(RebaseOutcome)} variant: {GetType().Name}"),
    };

    public const string UpToDateDiscriminator = "up_to_date";
    public const string RebasedDiscriminator = "rebased";
    public const string DisabledDiscriminator = "disabled";
    public const string SkippedDiscriminator = "skipped";
    public const string ConflictDiscriminator = "conflict";

    /// <summary>
    /// Rebuilds an outcome from its <paramref name="discriminator"/> (and, for <see cref="Skipped"/>, its
    /// <paramref name="reason"/>). Returns <see langword="null"/> for an unknown discriminator.
    /// </summary>
    public static RebaseOutcome? FromDiscriminator(string discriminator, string? reason) =>
        discriminator switch
        {
            UpToDateDiscriminator => new UpToDate(),
            RebasedDiscriminator => new Rebased(),
            DisabledDiscriminator => new Disabled(),
            SkippedDiscriminator => new Skipped(string.IsNullOrWhiteSpace(reason) ? "unknown" : reason),
            ConflictDiscriminator => new Conflict(),
            _ => null,
        };

    /// <summary>The branch had no commits to catch up; no rebase was attempted.</summary>
    public sealed record UpToDate : RebaseOutcome;

    /// <summary>The provider rebased the branch and the new head was observed.</summary>
    public sealed record Rebased : RebaseOutcome;

    /// <summary>The repository's Rebase After Run toggle was off when the rebase step ran.</summary>
    public sealed record Disabled : RebaseOutcome;

    /// <summary>The rebase did not happen for <see cref="Reason"/> (fail open).</summary>
    public sealed record Skipped(string Reason) : RebaseOutcome;

    /// <summary>The provider refused the rebase because it conflicts with the default branch.</summary>
    public sealed record Conflict : RebaseOutcome;
}
