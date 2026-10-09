using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Domain.Entities;

public sealed class GitLabCredential : Credential
{
    // Private parameterless constructor for EF Core materialization.
    private GitLabCredential() : base(CredentialId.New())
    {
    }

    private GitLabCredential(CredentialId id) : base(id)
    {
    }

    public override Uri ApiBaseUrl => DeriveApiBaseUrl(BaseUrl);

    public static Uri DeriveApiBaseUrl(BaseUrl baseUrl) =>
        new(baseUrl.Value.ToString().TrimEnd('/') + "/api/v4");

    public static GitLabCredential Create(string name, string? token, BaseUrl baseUrl)
    {
        GuardAgainstQueryOrFragment(baseUrl);

        return new GitLabCredential(CredentialId.New())
        {
            Name = name,
            Token = token is not null ? new ProviderToken.Present(token) : null,
            BaseUrl = baseUrl,
            Host = baseUrl.Value.Host,
        };
    }

    /// <summary>
    /// Constructs a <see cref="GitLabCredential"/> carrying an <see cref="ProviderToken.Unreadable"/>
    /// token for unit-test purposes. The production path to <see cref="ProviderToken.Unreadable"/>
    /// is EF materialization of garbage ciphertext, exercised by the integration test
    /// <c>WhenAccountHasGarbageCiphertext</c> in
    /// <c>tests/Foundry.IntegrationTests/Modules/Monitoring/Endpoints/GetAccountsTests/</c>.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    internal static GitLabCredential CreateWithUnreadableToken(string name, BaseUrl baseUrl)
    {
        GitLabCredential credential = new(CredentialId.New());
        ApplyUnreadableToken(credential, name, baseUrl);
        return credential;
    }

    public void Update(string name, string? token, BaseUrl baseUrl)
    {
        GuardAgainstQueryOrFragment(baseUrl);

        Name = name;
        BaseUrl = baseUrl;
        Host = baseUrl.Value.Host;

        if (token is not null)
        {
            Token = new ProviderToken.Present(token);
        }
    }

    private static void GuardAgainstQueryOrFragment(BaseUrl baseUrl)
    {
        if (!string.IsNullOrEmpty(baseUrl.Value.Query) || !string.IsNullOrEmpty(baseUrl.Value.Fragment))
        {
            throw new ArgumentException(
                $"Base URL must not contain a query string or fragment, but was '{baseUrl.Value}'.",
                nameof(baseUrl));
        }
    }
}
