using System.Diagnostics;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// Closed union recording why an issue is waiting on its pull request's checks. It is shown in the issue pane
/// and never changes how the checks gate behaves.
/// </summary>
public abstract record ChecksPendingReason
{
    private ChecksPendingReason()
    {
    }

    /// <summary>
    /// A stable string token for each variant, suitable for persistence and wire encoding.
    /// </summary>
    public string Discriminator => this switch
    {
        AfterRun => AfterRunDiscriminator,
        HeadMoved => HeadMovedDiscriminator,
        ChecksRerun => ChecksRerunDiscriminator,
        CatchUp => CatchUpDiscriminator,
        BeforeReady => BeforeReadyDiscriminator,
        _ => throw new UnreachableException($"Unknown {nameof(ChecksPendingReason)} variant: {GetType().Name}"),
    };

    public const string AfterRunDiscriminator = "after_run";
    public const string HeadMovedDiscriminator = "head_moved";
    public const string ChecksRerunDiscriminator = "checks_rerun";
    public const string CatchUpDiscriminator = "catch_up";
    public const string BeforeReadyDiscriminator = "before_ready";

    /// <summary>
    /// Rebuilds a reason from its <paramref name="discriminator"/>. Returns <see langword="null"/> for an unknown
    /// discriminator.
    /// </summary>
    public static ChecksPendingReason? FromDiscriminator(string discriminator) =>
        discriminator switch
        {
            AfterRunDiscriminator => new AfterRun(),
            HeadMovedDiscriminator => new HeadMoved(),
            ChecksRerunDiscriminator => new ChecksRerun(),
            CatchUpDiscriminator => new CatchUp(),
            BeforeReadyDiscriminator => new BeforeReady(),
            _ => null,
        };

    /// <summary>A worker run or revision left an open pull request, which the post-run rebase handed to the gate.</summary>
    public sealed record AfterRun : ChecksPendingReason;

    /// <summary>New commits reached a review issue's pull request after its checks verdict.</summary>
    public sealed record HeadMoved : ChecksPendingReason;

    /// <summary>A review issue whose checks verdict was not passed read green on the same commits after a rerun.</summary>
    public sealed record ChecksRerun : ChecksPendingReason;

    /// <summary>
    /// The head of the catch-up queue rebased after its merge was refused because it fell behind the default branch;
    /// its checks run again on the rebased commits before it merges (ADR 0111).
    /// </summary>
    public sealed record CatchUp : ChecksPendingReason;

    /// <summary>
    /// A pull request a person could merge rebased before it was marked ready; its checks run again on the rebased
    /// commits before it enters review (ADR 0128).
    /// </summary>
    public sealed record BeforeReady : ChecksPendingReason;
}
