using System.Diagnostics;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// Closed union recording how the review step resolved before the issue reached a person's review without the
/// reviewer's verdict (ADR 0113). Persisted on review issues; <see langword="null"/> when the issue never went through
/// the review step or its verdict routed it.
/// </summary>
public abstract record ReviewOutcome
{
    private ReviewOutcome()
    {
    }

    /// <summary>
    /// A stable string token for each variant, suitable for persistence and wire encoding.
    /// </summary>
    public string Discriminator => this switch
    {
        Skipped => SkippedDiscriminator,
        _ => throw new UnreachableException($"Unknown {nameof(ReviewOutcome)} variant: {GetType().Name}"),
    };

    public const string SkippedDiscriminator = "skipped";

    /// <summary>
    /// Rebuilds an outcome from its <paramref name="discriminator"/> and, for <see cref="Skipped"/>, its
    /// <paramref name="reason"/>. Returns <see langword="null"/> for an unknown discriminator.
    /// </summary>
    public static ReviewOutcome? FromDiscriminator(string discriminator, string? reason) =>
        discriminator switch
        {
            SkippedDiscriminator => new Skipped(string.IsNullOrWhiteSpace(reason) ? "unknown" : reason),
            _ => null,
        };

    /// <summary>
    /// The review run failed and will not be retried automatically, so the review failed open to a person (ADR 0113).
    /// <see cref="Reason"/> is the run's failure category token, such as <c>verdict_invalid</c>.
    /// </summary>
    public sealed record Skipped(string Reason) : ReviewOutcome;
}
