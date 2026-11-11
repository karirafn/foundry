namespace Foundry.Modules.Workers.Contracts;

public static class WorkerImageNames
{
    public const string BaseImageTag = "foundry-claude-base:local";
    public const string LoginImageName = "foundry-claude-login:local";

    /// <summary>The Publisher image (ADR 0100): the only image a provider token runs in.</summary>
    public const string PublisherImageName = "foundry-publisher:local";
}
