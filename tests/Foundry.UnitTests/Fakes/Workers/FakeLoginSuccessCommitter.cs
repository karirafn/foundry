using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// Scriptable fake of <see cref="ILoginSuccessCommitter"/> for unit-testing
/// the login state machine without a real DB or event dispatcher.
/// </summary>
internal sealed class FakeLoginSuccessCommitter : ILoginSuccessCommitter
{
    private readonly bool _shouldThrow;

    public FakeLoginSuccessCommitter(bool shouldThrow = false)
    {
        _shouldThrow = shouldThrow;
    }

    public AccountIdentity? CommittedIdentity { get; private set; }

    public int CommitCallCount { get; private set; }

    public Task CommitAsync(AccountIdentity identity, CancellationToken cancellationToken)
    {
        CommitCallCount++;

        if (_shouldThrow)
        {
            throw new InvalidOperationException("Simulated commit failure.");
        }

        CommittedIdentity = identity;
        return Task.CompletedTask;
    }
}
