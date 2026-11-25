namespace Foundry.Modules.Settings.Domain.ValueObjects;

internal abstract record ImageBuildState
{
    private ImageBuildState() { }

    internal sealed record Idle : ImageBuildState;

    internal sealed record Building : ImageBuildState;

    internal sealed record Failed(
        string? ErrorTail,
        DateTimeOffset? NextRetryAt,
        int Attempt) : ImageBuildState;

    /// <summary>
    /// A scheduled rebuild failed while the previous verified image stays usable, so dispatch continues (ADR 0125).
    /// </summary>
    internal sealed record ScheduledRebuildFailed(
        string? ErrorTail,
        DateTimeOffset? NextRetryAt,
        int Attempt) : ImageBuildState;
}
