using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Login;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// No-op broadcaster for tests that do not assert on broadcast behavior.
/// </summary>
internal sealed class NullLoginSessionBroadcaster : ILoginSessionBroadcaster
{
    public static readonly NullLoginSessionBroadcaster Instance = new();

    public Task BroadcastAsync(LoginSessionUpdate update, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
