namespace Foundry.Modules.Credentials.Contracts;

/// <summary>
/// The credential workers use, in the form an Anthropic API request carries it. The secret never appears in
/// <see cref="object.ToString"/>.
/// </summary>
public abstract record ClaudeApiCredential
{
    private ClaudeApiCredential() { }

    /// <summary>The secret value sent with the request.</summary>
    public abstract string Secret { get; }

    /// <summary>An OAuth access token, sent as <c>Authorization: Bearer &lt;token&gt;</c>.</summary>
    public sealed record OAuthToken : ClaudeApiCredential
    {
        public OAuthToken(string token)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);
            Secret = token;
        }

        public override string Secret { get; }

        public override string ToString() => "OAuthToken { Secret = *** }";
    }

    /// <summary>An API key, sent as <c>x-api-key</c>.</summary>
    public sealed record ApiKey : ClaudeApiCredential
    {
        public ApiKey(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            Secret = key;
        }

        public override string Secret { get; }

        public override string ToString() => "ApiKey { Secret = *** }";
    }
}
