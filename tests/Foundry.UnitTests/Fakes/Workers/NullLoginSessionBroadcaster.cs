using Foundry.Modules.Credentials.Contracts;

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
