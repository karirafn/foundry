namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// A monitored repository no account's Namespace Claim covers on its host. <see cref="Violation"/> is the
/// <c>no-credential:&lt;namespace&gt;</c> rule naming the namespace an account must claim.
/// </summary>
public sealed record UnclaimedRepositorySummary(
    Guid Id,
    string Host,
    string Slug,
    string Violation,
    string ViolationDescription);
