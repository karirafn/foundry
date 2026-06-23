namespace Foundry.Modules.Settings.Domain;

public abstract record ImageBuildState
{
    private ImageBuildState() { }

    public sealed record Idle : ImageBuildState;

    public sealed record Building : ImageBuildState;

    public sealed record Failed(string? ErrorTail) : ImageBuildState;
}
