namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// The one reminder Foundry posts in the question's thread once the question has waited the reminder delay without
/// input (ADR 0096). It is posted at most once per round; a failed post retries with the question post's backoff.
/// </summary>
public abstract record QuestionReminder
{
    private QuestionReminder()
    {
    }

    /// <summary>Not posted yet.</summary>
    public sealed record Unsent : QuestionReminder;

    /// <summary>Posted as provider comment <paramref name="CommentId"/> at <paramref name="SentAt"/>.</summary>
    public sealed record Sent(long CommentId, DateTimeOffset SentAt) : QuestionReminder;

    /// <summary>
    /// The last of <paramref name="Attempts"/> posts failed. A transient failure retries at
    /// <paramref name="NextAttemptAt"/>; a permanent one has none and is not retried.
    /// </summary>
    public sealed record Failed(string Reason, DateTimeOffset? NextAttemptAt, int Attempts) : QuestionReminder;

    /// <summary>
    /// When the reminder should be posted for a question posted at <paramref name="postedAt"/>: the
    /// <paramref name="delay"/> after it while unsent, at the retry time after a transient failure, and never
    /// (<see langword="null"/>) once sent or after a permanent failure.
    /// </summary>
    public DateTimeOffset? DueAt(DateTimeOffset postedAt, TimeSpan delay) => this switch
    {
        Unsent => postedAt + delay,
        Failed failed => failed.NextAttemptAt,
        _ => null,
    };

    /// <summary>The status after a failed post, backing off as a failed question post does.</summary>
    public Failed Fail(string reason, bool transient, DateTimeOffset now)
    {
        int attempts = this is Failed failed ? failed.Attempts + 1 : 1;

        return new Failed(reason, transient ? QuestionPost.NextAttemptAfter(attempts, now) : null, attempts);
    }
}
