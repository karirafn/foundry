using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ImageBuild;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageBuildServiceTests;

public sealed class StartingAsync
{
    [Fact]
    public async Task WhenEnabledFalse_DoesNotCallDocker()
    {
        // Arrange
        SpyImageOperations spyImages = new();
        WorkerImageBuildService sut = BuildService(
            spyImages,
            imageBuildOptions: new ImageBuildOptions { Enabled = false });

        // Act
        await ((IHostedLifecycleService)sut).StartingAsync(CancellationToken.None);

        // Assert
        spyImages.BuildCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenContextPathDoesNotExist_Throws()
    {
        // Arrange
        string nonExistentPath = Path.Combine(
            Path.GetTempPath(),
            $"foundry-test-{Guid.NewGuid()}",
            "missing-context");

        WorkerImageBuildService sut = BuildService(
            new SpyImageOperations(),
            contentRootPath: Path.GetTempPath(),
            imageBuildOptions: new ImageBuildOptions
            {
                Enabled = true,
                ContextPath = nonExistentPath,
            });

        // Act
        InvalidOperationException ex = await Should.ThrowAsync<InvalidOperationException>(
            () => ((IHostedLifecycleService)sut).StartingAsync(CancellationToken.None));

        // Assert
        ex.Message.ShouldContain("context");
    }

    [Fact]
    public async Task WhenEnabled_CallsDockerBuildWithConfiguredTag()
    {
        // Arrange
        string contextDir = Path.Combine(Path.GetTempPath(), $"foundry-test-ctx-{Guid.NewGuid()}");
        Directory.CreateDirectory(contextDir);

        try
        {
            await File.WriteAllTextAsync(
    Path.Combine(contextDir, "Dockerfile"),
    "FROM scratch",
    TestContext.Current.CancellationToken);

            SpyImageOperations spyImages = new();
            WorkerImageBuildService sut = BuildService(
                spyImages,
                contentRootPath: Path.GetTempPath(),
                workerImage: "my-registry/worker:v1.0",
                imageBuildOptions: new ImageBuildOptions
                {
                    Enabled = true,
                    ContextPath = contextDir,
                });

            // Act
            await ((IHostedLifecycleService)sut).StartingAsync(CancellationToken.None);

            // Assert
            spyImages.BuildCalled.ShouldBeTrue();
            spyImages.LastParameters.ShouldNotBeNull();
            spyImages.LastParameters!.Tags.ShouldContain("my-registry/worker:v1.0");
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenEnabled_PassesBuildArgsToDocker()
    {
        // Arrange
        string contextDir = Path.Combine(Path.GetTempPath(), $"foundry-test-ctx-{Guid.NewGuid()}");
        Directory.CreateDirectory(contextDir);

        try
        {
            await File.WriteAllTextAsync(
    Path.Combine(contextDir, "Dockerfile"),
    "FROM scratch",
    TestContext.Current.CancellationToken);

            SpyImageOperations spyImages = new();
            WorkerImageBuildService sut = BuildService(
                spyImages,
                contentRootPath: Path.GetTempPath(),
                imageBuildOptions: new ImageBuildOptions
                {
                    Enabled = true,
                    ContextPath = contextDir,
                    BuildArgs = new Dictionary<string, string>
                    {
                        ["INSTALL_DOTNET"] = "true",
                        ["INSTALL_ANGULAR"] = "false",
                    },
                });

            // Act
            await ((IHostedLifecycleService)sut).StartingAsync(CancellationToken.None);

            // Assert
            spyImages.LastParameters.ShouldNotBeNull();
            spyImages.LastParameters!.BuildArgs.ShouldContainKey("INSTALL_DOTNET");
            spyImages.LastParameters!.BuildArgs["INSTALL_DOTNET"].ShouldBe("true");
            spyImages.LastParameters!.BuildArgs.ShouldContainKey("INSTALL_ANGULAR");
            spyImages.LastParameters!.BuildArgs["INSTALL_ANGULAR"].ShouldBe("false");
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    private static WorkerImageBuildService BuildService(
        IImageOperations imageOperations,
        string? contentRootPath = null,
        string workerImage = "ghcr.io/anthropics/claude-code:v1.0",
        ImageBuildOptions? imageBuildOptions = null)
    {
        WorkerOptions workerOptions = new()
        {
            ApiKey = "test-key",
            Image = workerImage,
            ImageBuild = imageBuildOptions ?? new ImageBuildOptions { Enabled = false },
        };

        StubHostEnvironment hostEnvironment = new(contentRootPath ?? Path.GetTempPath());

        return new WorkerImageBuildService(
            imageOperations,
            hostEnvironment,
            Options.Create(workerOptions),
            NullLogger<WorkerImageBuildService>.Instance);
    }

    private sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Foundry.WebApi";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
    }

    private sealed class SpyImageOperations : IImageOperations
    {
        public bool BuildCalled { get; private set; }
        public ImageBuildParameters? LastParameters { get; private set; }

        public Task BuildImageFromDockerfileAsync(
            ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<JSONMessage> progress,
            CancellationToken cancellationToken)
        {
            BuildCalled = true;
            LastParameters = parameters;
            return Task.CompletedTask;
        }

#pragma warning disable CS0618 // Required for interface compliance — production code uses the non-obsolete overload
        public Task<Stream> BuildImageFromDockerfileAsync(
            Stream contents,
            ImageBuildParameters parameters,
            CancellationToken cancellationToken)
        {
            BuildCalled = true;
            LastParameters = parameters;
            return Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
        }
#pragma warning restore CS0618

        // Minimal stub implementations for interface compliance
        public Task<IList<ImagesListResponse>> ListImagesAsync(ImagesListParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImagesListResponse>>([]);
        public Task<ImageInspectResponse> InspectImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult(new ImageInspectResponse());
        public Task<IList<IDictionary<string, string>>> DeleteImageAsync(string name, ImageDeleteParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<IDictionary<string, string>>>([]);
        public Task<IList<ImageSearchResponse>> SearchImagesAsync(ImagesSearchParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImageSearchResponse>>([]);
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadImageAsync(ImageLoadParameters parameters, Stream imageStream, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Stream> SaveImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task<Stream> SaveImagesAsync(string[] names, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task TagImageAsync(string name, ImageTagParameters parameters, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PushImageAsync(string name, ImagePushParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ImagesPruneResponse> PruneImagesAsync(ImagesPruneParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new ImagesPruneResponse());
        public Task<CommitContainerChangesResponse> CommitContainerChangesAsync(CommitContainerChangesParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new CommitContainerChangesResponse());
        public Task<IList<ImageHistoryResponse>> GetImageHistoryAsync(string name, CancellationToken cancellationToken) => Task.FromResult<IList<ImageHistoryResponse>>([]);
    }
}
