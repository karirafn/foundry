namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// Records the outcome of posting an awaiting-input issue's questions (ADR 0096). Both calls only stage the change on
/// the shared unit of work: the caller's save commits it, together with the posted-comment registry entry, so the
/// question is marked posted in the same save that registers its comment.
/// </summary>
public interface IAwaitingInputCommands
{
    Task RecordQuestionPostedAsync(
        IssueId issueId,
        long commentId,
        DateTimeOffset postedAt,
        string? commentUrl,
        CancellationToken cancellationToken);

    Task RecordQuestionPostFailedAsync(
        IssueId issueId,
        string reason,
        bool transient,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
