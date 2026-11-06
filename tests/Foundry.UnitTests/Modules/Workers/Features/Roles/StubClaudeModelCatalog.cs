using Foundry.Modules.Workers.Features.Roles.Models;

namespace Foundry.UnitTests.Modules.Workers.Features.Roles;

internal sealed class StubClaudeModelCatalog(ClaudeModelList list) : IClaudeModelCatalog
{
    public int GetCallCount { get; private set; }

    public ClaudeModelList Current => list;

    public Task<ClaudeModelList> GetAsync(CancellationToken cancellationToken)
    {
        GetCallCount++;
        return Task.FromResult(list);
    }

    public Task<ClaudeModelList> RefreshAsync(CancellationToken cancellationToken) => Task.FromResult(list);
}
