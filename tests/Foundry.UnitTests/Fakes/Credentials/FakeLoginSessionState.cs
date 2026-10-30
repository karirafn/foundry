using Foundry.Modules.Credentials.Features.Login;

namespace Foundry.UnitTests.Fakes.Credentials;

/// <summary>
/// Scriptable in-memory fake of <see cref="ILoginSessionState"/> for unit tests.
/// </summary>
internal sealed class FakeLoginSessionState(bool isLoginActive = false) : ILoginSessionState
{
    public bool IsLoginActive { get; set; } = isLoginActive;
}
