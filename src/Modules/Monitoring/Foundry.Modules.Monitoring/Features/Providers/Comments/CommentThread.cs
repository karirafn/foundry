namespace Foundry.Modules.Monitoring.Features.Providers.Comments;

/// <summary>The thread a comment is posted to: a pull/merge request's conversation or an issue's.</summary>
internal abstract record CommentThread
{
    private CommentThread()
    {
    }

    public sealed record PullRequest(string PullRequestUrl) : CommentThread;

    public sealed record Issue(int IssueNumber) : CommentThread;
}
