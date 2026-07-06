using Foundry.Modules.Credentials.Infrastructure;

namespace Foundry.Modules.Credentials.Features.Login;

/// <summary>
/// Commits the side-effects of a successful OAuth login: persists the account identity
/// and publishes <c>CredentialsValidated</c> so consumers can react to the credential update.
/// </summary>
/// <remarks>
/// Injected into <see cref="LoginSessionService"/> so the state machine's
/// transitions and timeouts are unit-testable with a fake, while the real
/// DB mutation and event dispatch are tested separately.
/// </remarks>
public interface ILoginSuccessCommitter
{
    Task CommitAsync(AccountIdentity identity, CancellationToken cancellationToken);
}
