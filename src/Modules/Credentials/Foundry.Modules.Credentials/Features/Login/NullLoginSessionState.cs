namespace Foundry.Modules.Credentials.Features.Login;

/// <summary>
/// Placeholder <see cref="ILoginSessionState"/> registered until the full login session is
/// moved into the Credentials module (step 5). Always reports login inactive.
/// </summary>
internal sealed class NullLoginSessionState : ILoginSessionState
{
    public bool IsLoginActive => false;
}
