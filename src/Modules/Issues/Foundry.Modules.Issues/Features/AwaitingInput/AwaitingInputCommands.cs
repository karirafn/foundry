using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.AwaitingInput;

internal sealed class AwaitingInputCommands(
    DbContext db,
    ILogger<AwaitingInputCommands> logger) : IAwaitingInputCommands
{
    public async Task RecordQuestionPostedAsync(
        IssueId issueId,
        long commentId,
        DateTimeOffset postedAt,
        string? commentUrl,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(issueId, cancellationToken) is { } awaiting)
        {
            awaiting.RecordQuestionPosted(commentId, postedAt, commentUrl);
        }
    }

    public async Task RecordQuestionPostFailedAsync(
        IssueId issueId,
        string reason,
        bool transient,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(issueId, cancellationToken) is { } awaiting)
        {
            awaiting.RecordQuestionPostFailed(reason, transient, now);
        }
    }

    private async Task<AwaitingInputIssue?> FindAsync(IssueId issueId, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>().FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

        if (issue is AwaitingInputIssue awaiting)
        {
            return awaiting;
        }

        logger.LogWarning(
            "Question post result for issue {IssueId} ignored: the issue is no longer awaiting input (state: {State}).",
            issueId,
            issue?.GetType().Name ?? "not found");
        return null;
    }
}
