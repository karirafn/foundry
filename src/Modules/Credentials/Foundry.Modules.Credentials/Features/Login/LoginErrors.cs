using Foundry.Shared;

namespace Foundry.Modules.Credentials.Features.Login;

/// <summary>
/// Well-known error constants for the login session feature.
/// </summary>
internal static class LoginErrors
{
    internal const string NoActiveSessionCode = "Login.NoActiveSession";
    internal const string NotAcceptingCodeCode = "Login.NotAcceptingCode";

    internal static readonly Error NoActiveSession = new(
        NoActiveSessionCode,
        "No active login session. Start a new session first.");

    internal static readonly Error NotAcceptingCode = new(
        NotAcceptingCodeCode,
        "The current session is not waiting for an authorization code.");
}
