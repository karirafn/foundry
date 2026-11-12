using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;

/// <summary>
/// Provider-neutral failures of <see cref="IIssueProvider.AddBlockerAsync"/>. A transient failure (5xx, 429,
/// a rate-limited 403, a transport failure) may succeed on retry; a permanent one (any other 4xx) will not.
/// A duplicate-link response counts as success only when a read-back of the issue's links confirms the
/// blocking relation. Callers own the retry policy.
/// </summary>
internal static class BlockerLinkErrors
{
    public const string TransientCode = "BlockerLink.Transient";
    public const string PermanentCode = "BlockerLink.Permanent";

    public static Error Transient(string message) => new(TransientCode, message);

    public static Error Permanent(string message) => new(PermanentCode, message);

    public static Error NotSupported() =>
        Permanent("This provider does not support linking blocking issues.");

    public static bool IsTransient(Error error) =>
        string.Equals(error.Code, TransientCode, StringComparison.Ordinal);

    /// <summary>
    /// Whether an exception from calling a provider is a transport failure (connection error, timeout) that
    /// carries no provider verdict. A parse or programming error is not one, so it surfaces instead of retrying.
    /// </summary>
    public static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);
}
