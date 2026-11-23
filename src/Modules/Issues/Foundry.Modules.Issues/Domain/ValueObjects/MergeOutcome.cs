using System.Diagnostics;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// Closed union recording how Foundry's own merge of the pull request resolved before the issue left the
/// merge-pending state (ADR 0105). Persisted on review issues; a merged pull request completes the issue instead.
/// </summary>
public abstract record MergeOutcome
{
    private MergeOutcome()
    {
    }

    /// <summary>
    /// A stable string token for each variant, suitable for persistence and wire encoding.
    /// </summary>
    public string Discriminator => this switch
    {
        Merged => MergedDiscriminator,
        Disabled => DisabledDiscriminator,
        Skipped => SkippedDiscriminator,
        _ => throw new UnreachableException($"Unknown {nameof(MergeOutcome)} variant: {GetType().Name}"),
    };

    public const string MergedDiscriminator = "merged";
    public const string DisabledDiscriminator = "disabled";
    public const string SkippedDiscriminator = "skipped";

    /// <summary>
    /// Rebuilds an outcome from its <paramref name="discriminator"/> (and, for <see cref="Skipped"/>, its
    /// <paramref name="reason"/>). Returns <see langword="null"/> for an unknown discriminator.
    /// </summary>
    public static MergeOutcome? FromDiscriminator(string discriminator, string? reason) =>
        discriminator switch
        {
            MergedDiscriminator => new Merged(),
            DisabledDiscriminator => new Disabled(),
            SkippedDiscriminator => new Skipped(string.IsNullOrWhiteSpace(reason) ? "unknown" : reason),
            _ => null,
        };

    /// <summary>The provider merged the pull request.</summary>
    public sealed record Merged : MergeOutcome;

    /// <summary>The repository's Merge When Checks Pass setting was off when the merge step ran.</summary>
    public sealed record Disabled : MergeOutcome;

    /// <summary>
    /// The merge did not happen for <see cref="Reason"/>: <c>not_mergeable</c>, <c>unauthorized</c>,
    /// <c>head_moved</c>, <c>provider_error</c>, <c>fell_behind</c>, <c>catch_up_failed</c> or <c>draft</c>.
    /// </summary>
    public sealed record Skipped(string Reason) : MergeOutcome;
}
