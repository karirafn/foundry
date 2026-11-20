namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>
/// The pull request's description as a review run's dispatch read it from the provider: its text, or that it could not
/// be read. Read per dispatch for the review prompt and never persisted, so no check after the run depends on it.
/// </summary>
internal abstract record LivePullRequestBody
{
    private LivePullRequestBody()
    {
    }

    /// <summary>The description the provider returned; empty when the pull request has none.</summary>
    internal sealed record Read(string Text) : LivePullRequestBody;

    internal sealed record Unreadable : LivePullRequestBody;
}
