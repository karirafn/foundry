namespace Foundry.Modules.Monitoring.Features.Providers.Publishing;

/// <summary>What a provider answered to a request to open a pull/merge request.</summary>
internal abstract record PullRequestCreation
{
    private PullRequestCreation()
    {
    }

    public sealed record Opened(string WebUrl) : PullRequestCreation;

    /// <summary>The provider refused because a pull/merge request is already open for the source branch.</summary>
    public sealed record AlreadyOpen : PullRequestCreation;
}
