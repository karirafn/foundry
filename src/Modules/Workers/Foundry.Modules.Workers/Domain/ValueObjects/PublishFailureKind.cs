namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>How a publish attempt failed, as the Publisher's <c>kind=</c> field names it.</summary>
public enum PublishFailureKind
{
    /// <summary>A retry would fail the same way (an invalid bundle, a refused token), so it waits for an operator.</summary>
    Permanent,

    /// <summary>A retry may succeed (a moved branch, a provider hiccup), so it is retried with backoff.</summary>
    Transient,

    /// <summary>
    /// The provider could not be reached at all (DNS, a refused connection, 429 or 5xx). Retried like
    /// <see cref="Transient"/>, and it pauses the run's provider host until a probe reaches it again (#885).
    /// </summary>
    Unreachable,
}
