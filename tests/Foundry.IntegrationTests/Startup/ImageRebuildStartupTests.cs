using Foundry.IntegrationTests.Smoke;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Startup;

/// <summary>
/// A first boot must build the worker image: the role catalog is published only by that build, and
/// dispatch waits for the catalog.
/// </summary>
public sealed class ImageRebuildStartupTests : IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    private readonly ComposedHostBoundaries _boundaries = new();
    private readonly FoundryWebAppFactory _factory;

    public ImageRebuildStartupTests()
    {
        _factory = _boundaries.CreateHost();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await ((IAsyncDisposable)_factory).DisposeAsync();
        _boundaries.Dispose();
    }

    [Fact]
    public async Task WhenDatabaseIsFresh_StartupBuildsTheWorkerImage()
    {
        // Act
        _ = _factory.CreateClient();

        // Assert
        await Should.NotThrowAsync(() => _boundaries.ImageBuild.Started.WaitAsync(StartupTimeout, TestContext.Current.CancellationToken));
    }
}
