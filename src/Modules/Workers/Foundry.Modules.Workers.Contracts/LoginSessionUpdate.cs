namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Payload broadcast over SignalR to dashboard clients on each OAuth login session phase transition.
/// </summary>
public sealed record LoginSessionUpdate(
    Guid SessionId,
    string Phase,
    string? AuthorizationUrl,
    string? FailureReason,
    string? FailureMessage);

/// <summary>
/// Discriminator string constants for <see cref="LoginSessionUpdate.Phase"/>.
/// </summary>
public static class LoginPhaseDiscriminator
{
    public const string Starting = "Starting";
    public const string WaitingForAuthorization = "WaitingForAuthorization";
    public const string SigningIn = "SigningIn";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

/// <summary>
/// Discriminator string constants for <see cref="LoginSessionUpdate.FailureReason"/>.
/// Maps to UI copy in the frontend <c>OAuthPanelComponent</c>.
/// </summary>
public static class LoginFailureDiscriminator
{
    public const string InvalidCode = "InvalidCode";
    public const string UrlTimeout = "UrlTimeout";
    public const string CodeTimeout = "CodeTimeout";
    public const string Unknown = "Unknown";
}
