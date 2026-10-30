namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Abstraction over <see cref="UsagePollCoordinator"/> allowing unit tests to stub
/// the poll without Docker or a real database.
/// </summary>
internal interface IUsagePollCoordinator
{
    /// <summary>
    /// Runs a usage poll. If a poll is already in flight, the caller joins it and returns
    /// the snapshot produced by that poll (JOIN semantics, not fail-fast).
    /// Returns the current snapshot after the poll completes (or <see langword="null"/> when
    /// the snapshot was cleared or the account cannot report usage).
    /// </summary>
    Task<UsageSnapshot?> PollAsync(CancellationToken cancellationToken);
}
