using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Login;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// Records all <see cref="ILoginSessionBroadcaster"/> broadcasts for test assertion.
/// </summary>
internal sealed class CapturingLoginSessionBroadcaster : ILoginSessionBroadcaster
{
    private readonly List<LoginSessionUpdate> _captured = [];

    public IReadOnlyList<LoginSessionUpdate> Captured => _captured;

    public Task BroadcastAsync(LoginSessionUpdate update, CancellationToken cancellationToken)
    {
        _captured.Add(update);
        return Task.CompletedTask;
    }
}
