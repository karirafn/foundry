using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers.Comments;

/// <summary>
/// Provider-neutral failures of <see cref="IIssueProvider.PostCommentAsync"/>. A transient failure (5xx,
/// timeout, rate limit) may succeed on retry; a permanent one (any other 4xx, an invalid request) will not.
/// Callers own the retry policy.
/// </summary>
internal static class CommentPostErrors
{
    public const string TransientCode = "CommentPost.Transient";
    public const string PermanentCode = "CommentPost.Permanent";

    public static Error Transient(string message) => new(TransientCode, message);

    public static Error Permanent(string message) => new(PermanentCode, message);

    public static Error NotSupported() =>
        Permanent("This provider does not support posting comments.");

    public static bool IsTransient(Error error) =>
        string.Equals(error.Code, TransientCode, StringComparison.Ordinal);

    /// <summary>
    /// Whether an exception from sending a post is a transport failure (connection error, timeout) rather
    /// than the caller cancelling. A transport failure carries no provider verdict, so it is transient.
    /// </summary>
    public static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;
}
