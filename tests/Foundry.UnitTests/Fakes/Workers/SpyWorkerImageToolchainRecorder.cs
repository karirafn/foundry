using Foundry.Modules.Workers.Features.ImageBuild;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>Counts the image changes reported to the toolchain recorder.</summary>
internal sealed class SpyWorkerImageToolchainRecorder : IWorkerImageToolchainRecorder
{
    public int ImageChangedCount { get; private set; }

    public void ImageChanged() => ImageChangedCount++;
}
