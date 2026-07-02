using Foundry.Modules.Workers.Infrastructure;

namespace Foundry.Modules.Workers.Features.Login;

/// <summary>
/// Commits the side-effects of a successful OAuth login: persists the account identity
/// to <c>GlobalSettings</c>, clears <c>AuthInvalidPause</c>, and publishes
/// <c>DispatchResumed</c> so the Phase-1 handler re-queues auth-invalid issues.
/// </summary>
/// <remarks>
/// Injected into <see cref="LoginSessionService"/> so the state machine's
/// transitions and timeouts are unit-testable with a fake, while the real
/// DB mutation and event dispatch are tested separately.
/// </remarks>
internal interface ILoginSuccessCommitter
{
    Task CommitAsync(AccountIdentity identity, CancellationToken cancellationToken);
}
