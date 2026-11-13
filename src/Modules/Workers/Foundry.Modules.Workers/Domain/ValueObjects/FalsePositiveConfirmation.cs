namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The secret findings an operator confirmed as false positives on a run, and when they last confirmed (#899). It is
/// the record a published pull request carries, so it holds locations and rules only, never a matched value.
/// </summary>
public sealed record FalsePositiveConfirmation
{
    public FalsePositiveConfirmation(IReadOnlyList<SecretFinding> findings, DateTimeOffset confirmedAt)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (findings.Count == 0)
        {
            throw new ArgumentException("A confirmation confirms at least one finding.", nameof(findings));
        }

        Findings = findings;
        ConfirmedAt = confirmedAt;
    }

    public IReadOnlyList<SecretFinding> Findings { get; }

    public DateTimeOffset ConfirmedAt { get; }
}
