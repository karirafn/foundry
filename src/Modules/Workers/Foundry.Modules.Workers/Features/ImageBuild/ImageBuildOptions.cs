using System.Collections.Frozen;

namespace Foundry.Modules.Workers.Features.ImageBuild;

internal sealed class ImageBuildOptions
{
    public bool Enabled { get; set; } = true;

    public IReadOnlyDictionary<string, string> BuildArgs { get; set; } = FrozenDictionary<string, string>.Empty;

    public string ContextPath { get; set; } = "workers";
}
