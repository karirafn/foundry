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
}
