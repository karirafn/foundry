using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Domain.Entities;

public sealed class GitHubAccount : Account
{
    private static readonly Uri GitHubApiBaseUrl = new("https://api.github.com");

    public override Uri ApiBaseUrl => DeriveApiBaseUrl(BaseUrl);

    public static Uri DeriveApiBaseUrl(BaseUrl baseUrl) =>
        baseUrl.Value.Host == "github.com"
            ? GitHubApiBaseUrl
            : new Uri(baseUrl.Value.ToString().TrimEnd('/') + "/api/v3/");

    // Private parameterless constructor for EF Core materialization.
    private GitHubAccount() : base(AccountId.New())
    {
    }

    private GitHubAccount(AccountId id) : base(id)
    {
    }

    public static GitHubAccount Create(string name, string? token, BaseUrl baseUrl)
    {
        return new GitHubAccount(AccountId.New())
        {
            Name = name,
            Token = token,
            BaseUrl = baseUrl,
        };
    }

    public void Update(string name, string? token, BaseUrl baseUrl)
    {
        Name = name;
        BaseUrl = baseUrl;

        if (token is not null)
        {
            Token = token;
        }
    }
}
