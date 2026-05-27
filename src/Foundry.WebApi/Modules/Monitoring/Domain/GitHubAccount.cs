namespace Foundry.WebApi.Modules.Monitoring.Domain;

public sealed class GitHubAccount : Account
{
    // Private parameterless constructor for EF Core materialization.
    private GitHubAccount() : base(AccountId.New())
    {
    }

    private GitHubAccount(AccountId id) : base(id)
    {
    }

    public static GitHubAccount Create(string name, string secretKeyName, Uri baseUrl)
    {
        return new GitHubAccount(AccountId.New())
        {
            Name = name,
            SecretKeyName = secretKeyName,
            BaseUrl = baseUrl,
        };
    }
}
