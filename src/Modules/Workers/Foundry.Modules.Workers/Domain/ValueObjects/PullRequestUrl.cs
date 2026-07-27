namespace Foundry.Modules.Workers.Domain.ValueObjects;

public readonly record struct PullRequestUrl(string Value)
{
    public static PullRequestUrl From(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            throw new ArgumentException("Pull request URL must be a valid absolute URI.", nameof(value));
        }

        return new PullRequestUrl(value);
    }
}
