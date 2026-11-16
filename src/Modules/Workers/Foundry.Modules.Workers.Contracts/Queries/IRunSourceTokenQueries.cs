namespace Foundry.Modules.Workers.Contracts.Queries;

/// <summary>Checks a Run Source Token for the Package Source Proxy (ADR 0112).</summary>
public interface IRunSourceTokenQueries
{
    /// <summary>
    /// The monitored repository of the run whose Run Source Token hashes to <paramref name="tokenHash"/>, while that run
    /// is starting or active; otherwise <see langword="null"/>. Once the run's <c>claude</c> exits (publishing or any
    /// later state) its token no longer passes, so no revocation is needed.
    /// </summary>
    Task<Guid?> FindLiveRunRepositoryAsync(RunSourceTokenHash tokenHash, CancellationToken cancellationToken);
}
