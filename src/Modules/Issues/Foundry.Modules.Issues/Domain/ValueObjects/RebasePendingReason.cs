using System.Diagnostics;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// Closed union recording why an issue is rebasing its pull request onto the default branch. It decides where the
/// rebase leads: an <see cref="AfterRun"/> rebase always moves on to the checks gate (ADR 0087), a
/// <see cref="CatchUp"/> rebase serves a merge refused because the pull request fell behind (ADR 0111) and goes to
/// review when it cannot bring the branch up to date, and a <see cref="BeforeReady"/> rebase brings a pull request a
/// person could merge up to date before it enters review (ADR 0128).
/// </summary>
public abstract record RebasePendingReason
{
    private RebasePendingReason()
    {
    }

    /// <summary>
    /// A stable string token for each variant, suitable for persistence and wire encoding.
    /// </summary>
    public string Discriminator => this switch
    {
        AfterRun => AfterRunDiscriminator,
        CatchUp => CatchUpDiscriminator,
        BeforeReady => BeforeReadyDiscriminator,
        _ => throw new UnreachableException($"Unknown {nameof(RebasePendingReason)} variant: {GetType().Name}"),
    };

    public const string AfterRunDiscriminator = "after_run";
    public const string CatchUpDiscriminator = "catch_up";
    public const string BeforeReadyDiscriminator = "before_ready";

    /// <summary>
    /// Rebuilds a reason from its <paramref name="discriminator"/>. Returns <see langword="null"/> for an unknown
    /// discriminator.
    /// </summary>
    public static RebasePendingReason? FromDiscriminator(string discriminator) =>
        discriminator switch
        {
            AfterRunDiscriminator => new AfterRun(),
            CatchUpDiscriminator => new CatchUp(),
            BeforeReadyDiscriminator => new BeforeReady(CheckOnly: false),
            _ => null,
        };

    /// <summary>A worker run or revision left an open pull request, rebased before its checks are read.</summary>
    public sealed record AfterRun : RebasePendingReason;

    /// <summary>
    /// The pull request is the head of its repository's catch-up queue: its merge was refused because it fell behind
    /// the default branch, and it rebases before its checks run again and it merges.
    /// </summary>
    public sealed record CatchUp : RebasePendingReason;

    /// <summary>
    /// The pull request is one a person could merge as it stands, about to be marked ready: it rebases first, so a
    /// person receives it up to date with the default branch (ADR 0128). <paramref name="CheckOnly"/> is set once the
    /// issue spent <see cref="Entities.Issue.MaxBeforeReadyRounds"/>: the step then only reads whether the branch fell
    /// behind, and never rebases it.
    /// </summary>
    public sealed record BeforeReady(bool CheckOnly) : RebasePendingReason;
}
