namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// The Basic-auth credential Foundry sends to a Package Source: the provider username and a token.
/// <see cref="ToString"/> never shows the token, so a credential that reaches a log or an exception stays secret.
/// </summary>
internal sealed class UpstreamCredential
{
    public UpstreamCredential(string username, string token)
    {
        // Empty is refused; whitespace is kept, because a registered credential is sent exactly as the operator gave it.
        ArgumentException.ThrowIfNullOrEmpty(username);
        ArgumentException.ThrowIfNullOrEmpty(token);
        Username = username;
        Token = token;
    }

    public string Username { get; }

    public string Token { get; }

    public override string ToString() => $"{Username}:***";
}
