namespace Foundry.Modules.Workers.Features.ImageBuild;

internal sealed class ImageBuildOptions
{
    public bool Enabled { get; set; } = true;

    public IReadOnlyDictionary<string, string> BuildArgs { get; set; } = new Dictionary<string, string>();

    public string ContextPath { get; set; } = "workers";
}
