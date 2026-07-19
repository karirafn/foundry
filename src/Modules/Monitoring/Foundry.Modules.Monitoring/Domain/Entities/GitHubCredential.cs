using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Domain.Entities;

public sealed class GitHubCredential : Credential
{
    private static readonly Uri GitHubApiBaseUrl = new("https://api.github.com");

    public override Uri ApiBaseUrl => DeriveApiBaseUrl(BaseUrl);

    public static Uri DeriveApiBaseUrl(BaseUrl baseUrl) =>
        baseUrl.Value.Host == "github.com"
            ? GitHubApiBaseUrl
            : new Uri(baseUrl.Value.ToString().TrimEnd('/') + "/api/v3/");

    // Private parameterless constructor for EF Core materialization.
    private GitHubCredential() : base(CredentialId.New())
    {
    }

    private GitHubCredential(CredentialId id) : base(id)
    {
    }

    public static GitHubCredential Create(string name, string? token, BaseUrl baseUrl)
    {
        return new GitHubCredential(CredentialId.New())
        {
            Name = name,
            Token = token,
            BaseUrl = baseUrl,
            Host = baseUrl.Value.Host,
        };
    }

    public void Update(string name, string? token, BaseUrl baseUrl)
    {
        Name = name;
        BaseUrl = baseUrl;
        Host = baseUrl.Value.Host;

        if (token is not null)
        {
            Token = token;
        }
    }
}
