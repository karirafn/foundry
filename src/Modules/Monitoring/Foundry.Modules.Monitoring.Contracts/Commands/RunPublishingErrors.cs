using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts.Commands;

/// <summary>
/// Failures of <see cref="IRunPublishingCommands"/>. A transient failure (5xx, timeout, rate limit, a provider
/// that could not be reached) may succeed on retry; a permanent one (any other rejection, an invalid request,
/// a repository or credential Foundry cannot use) will not. A refused one is a permanent failure in which the provider
/// refused the account token access (404, or a 403 that is not rate limiting). Callers own the retry policy.
/// </summary>
public static class RunPublishingErrors
{
    public const string TransientCode = "RunPublishing.Transient";
    public const string PermanentCode = "RunPublishing.Permanent";
    public const string RefNotFoundCode = "RunPublishing.RefNotFound";
    public const string RefusedCode = "RunPublishing.Refused";

    public static Error Transient(string message) => new(TransientCode, message);

    public static Error Permanent(string message) => new(PermanentCode, message);

    public static Error Refused(string message) => new(RefusedCode, message);

    public static Error RefNotFound(string message) => new(RefNotFoundCode, message) { Kind = ErrorKind.NotFound };

    public static bool IsTransient(Error error) =>
        string.Equals(error.Code, TransientCode, StringComparison.Ordinal);
}
