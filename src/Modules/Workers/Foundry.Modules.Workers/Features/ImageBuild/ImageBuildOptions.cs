namespace Foundry.Modules.Workers.Features.ImageBuild;

internal sealed class ImageBuildOptions
{
    public bool Enabled { get; set; } = true;

    public string ContextPath { get; set; } = "workers";

    /// <summary>
    /// Initial backoff delay for the first automatic retry after a build failure.
    /// Doubles on each subsequent failure up to <see cref="MaxBackoff"/>.
    /// </summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum backoff delay cap. Retries will not be scheduled further than this from now.
    /// </summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(15);
}
