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
        _ => throw new UnreachableException($"Unknown {nameof(ChecksPendingReason)} variant: {GetType().Name}"),
    };

    public const string AfterRunDiscriminator = "after_run";
    public const string HeadMovedDiscriminator = "head_moved";

    /// <summary>
    /// Rebuilds a reason from its <paramref name="discriminator"/>. Returns <see langword="null"/> for an unknown
    /// discriminator.
    /// </summary>
    public static ChecksPendingReason? FromDiscriminator(string discriminator) =>
        discriminator switch
        {
            AfterRunDiscriminator => new AfterRun(),
            HeadMovedDiscriminator => new HeadMoved(),
            _ => null,
        };

    /// <summary>A worker run or revision left an open pull request, which the post-run rebase handed to the gate.</summary>
    public sealed record AfterRun : ChecksPendingReason;

    /// <summary>New commits reached a review issue's pull request after its checks verdict.</summary>
    public sealed record HeadMoved : ChecksPendingReason;
}
