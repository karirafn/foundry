using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Domain.Entities;

public sealed class GitHubAccount : Account
{
    private static readonly Uri GitHubApiBaseUrl = new("https://api.github.com");

    public override Uri ApiBaseUrl => BaseUrl.Host == "github.com"
        ? GitHubApiBaseUrl
        : new Uri(BaseUrl.ToString().TrimEnd('/') + "/api/v3/");

    // Private parameterless constructor for EF Core materialization.
    private GitHubAccount() : base(AccountId.New())
    {
    }

    private GitHubAccount(AccountId id) : base(id)
    {
    }

    public static GitHubAccount Create(string name, string secretKeyName, Uri baseUrl)
    {
        if (baseUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Base URL must use the HTTPS scheme.", nameof(baseUrl));
        }

        return new GitHubAccount(AccountId.New())
        {
            Name = name,
            SecretKeyName = secretKeyName,
            BaseUrl = baseUrl,
        };
    }
}
