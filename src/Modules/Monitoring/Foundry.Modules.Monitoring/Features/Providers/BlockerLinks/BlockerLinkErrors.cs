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
}
