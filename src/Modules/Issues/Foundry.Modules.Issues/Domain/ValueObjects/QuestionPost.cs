namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// Whether an awaiting-input issue's questions reached the provider (ADR 0096). The state is committed first and a
/// poll step posts afterwards, so a redelivered completion never posts twice; reply detection waits for
/// <see cref="Posted"/>.
/// </summary>
public abstract record QuestionPost
{
    internal static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMinutes(1);

    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    private QuestionPost()
    {
    }

    /// <summary>Not yet attempted.</summary>
    public sealed record Pending : QuestionPost;

    /// <summary>
    /// Posted as provider comment <paramref name="CommentId"/>; <paramref name="PostedAt"/> is the cutoff replies are
    /// read after, and <paramref name="CommentUrl"/> links to the comment when the provider's URL form is known.
    /// </summary>
    public sealed record Posted(long CommentId, DateTimeOffset PostedAt, string? CommentUrl) : QuestionPost;

    /// <summary>
    /// The last of <paramref name="Attempts"/> posts failed. A transient failure retries at
    /// <paramref name="NextAttemptAt"/>; a permanent one has none and is not retried automatically.
    /// </summary>
    public sealed record Failed(string Reason, DateTimeOffset? NextAttemptAt, int Attempts) : QuestionPost;

    /// <summary>Whether the poll step should post now.</summary>
    public bool IsDue(DateTimeOffset now) => this switch
    {
        Pending => true,
        Failed { NextAttemptAt: DateTimeOffset nextAttemptAt } => nextAttemptAt <= now,
        _ => false,
    };

    /// <summary>The status after a failed post: transient failures back off exponentially, capped at an hour.</summary>
    public Failed Fail(string reason, bool transient, DateTimeOffset now)
    {
        int attempts = this is Failed failed ? failed.Attempts + 1 : 1;

        if (!transient)
        {
            return new Failed(reason, NextAttemptAt: null, attempts);
        }

        double delayTicks = Math.Min(
            FirstRetryDelay.Ticks * Math.Pow(2, attempts - 1),
            MaxRetryDelay.Ticks);
        return new Failed(reason, now + TimeSpan.FromTicks((long)delayTicks), attempts);
    }
}
