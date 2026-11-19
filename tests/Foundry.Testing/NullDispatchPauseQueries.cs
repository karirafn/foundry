using Foundry.Modules.Workers.Contracts.Queries;

namespace Foundry.Testing;

/// <summary><see cref="IDispatchPauseQueries"/> for tests that need dispatch unpaused: no pause reason is set.</summary>
public sealed class NullDispatchPauseQueries : IDispatchPauseQueries
{
    public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
        => Task.FromResult(new DispatchPauseState(null, false, []));
}
