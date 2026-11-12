using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Domain.Entities.States;

/// <summary>
/// A run whose publish the Diff gate blocked because it found possible secrets in the Run commits (#788): nothing was
/// pushed. Its unpublished work waits in its Hand-off volume until an operator discards the run or the volume's time to
/// live does. There is no way to publish it anyway.
/// </summary>
public sealed class PublishBlockedRun : UnpublishedRun, IDiscardableRun
{
    // Private parameterless constructor for EF Core materialization.
    private PublishBlockedRun()
    {
    }

    private PublishBlockedRun(PublishingRun source)
        : base(source)
    {
    }

    /// <summary>Where the possible secrets are; the findings never hold a matched value.</summary>
    public IReadOnlyList<SecretFinding> SecretFindings { get; private set; } = [];

    public DateTimeOffset PublishBlockedAt { get; private set; }

    internal static PublishBlockedRun FromPublishing(
        PublishingRun publishing,
        IReadOnlyList<SecretFinding> findings,
        DateTimeOffset blockedAt) =>
        new(publishing)
        {
            SecretFindings = findings,
            PublishBlockedAt = blockedAt,
        };

    public FailedRun Discard() => DiscardWith(new FailureReason.PublishExpired());

    public FailedRun DiscardOnRequest() => DiscardWith(new FailureReason.RunDiscarded());
}
