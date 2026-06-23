namespace Foundry.Modules.Workers.Features.ImageBuild;

internal sealed class ImageBuildOptions
{
    public bool Enabled { get; set; } = true;

    public string ContextPath { get; set; } = "workers";
}
