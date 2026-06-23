using Foundry.Modules.Workers.Features.ImageBuild;

using Microsoft.Extensions.Hosting;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageBuildServiceTests;

public sealed class StartingAsync
{
    [Fact]
    public async Task StartingAsync_CompletesWithoutError()
    {
        // Arrange — startup build is now delegated to WorkerImageRebuildService
        WorkerImageBuildService sut = new();

        // Act / Assert
        await Should.NotThrowAsync(
            () => ((IHostedLifecycleService)sut).StartingAsync(CancellationToken.None));
    }
}
