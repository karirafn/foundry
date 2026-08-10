using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildServiceTests;

public sealed class StartingAsync
{
    private static WorkerImageRebuildService BuildService(
        SpyWorkerImageRebuildQueue? queue = null,
        bool imageBuildEnabled = true,
        bool settingsExists = true)
    {
        ServiceCollection services = new();
        services.AddScoped<IGlobalSettingsQueries>(_ => new StubGlobalSettingsQueries(settingsExists));
        services.AddScoped<IIntegrationEventDispatcher>(_ => new NullIntegrationEventDispatcher());

        ServiceProvider sp = services.BuildServiceProvider();

        WorkerOptions workerOptions = new()
        {
            Image = "test-image:latest",
            ImageBuild = new ImageBuildOptions
            {
                Enabled = imageBuildEnabled,
                ContextPath = string.Empty,
            },
        };

        return new WorkerImageRebuildService(
            queue ?? new SpyWorkerImageRebuildQueue(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            new NullImageOperations(),
            new StubHostEnvironment(string.Empty),
            Options.Create(workerOptions),
            NullLogger<WorkerImageRebuildService>.Instance);
    }

    [Fact]
    public async Task OnStartup_RequestsImmediateRebuild()
    {
        // Arrange
        SpyWorkerImageRebuildQueue queue = new();
        WorkerImageRebuildService sut = BuildService(queue, settingsExists: true);

        // Act
        await ((IHostedLifecycleService)sut).StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        queue.RequestImmediateRebuildCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenGlobalSettingsMissing_DoesNotRequestImmediateRebuild()
    {
        // Arrange — settings query returns null; early-return path fires
        SpyWorkerImageRebuildQueue queue = new();
        WorkerImageRebuildService sut = BuildService(queue, settingsExists: false);

        // Act
        await ((IHostedLifecycleService)sut).StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        queue.RequestImmediateRebuildCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenGlobalSettingsMissing_DoesNotThrow()
    {
        // Arrange — no settings row
        WorkerImageRebuildService sut = BuildService(settingsExists: false);

        // Act
        Task act = ((IHostedLifecycleService)sut).StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task WhenImageBuildDisabled_DoesNotRequestImmediateRebuild()
    {
        // Arrange
        SpyWorkerImageRebuildQueue queue = new();
        WorkerImageRebuildService sut = BuildService(queue, imageBuildEnabled: false);

        // Act
        await ((IHostedLifecycleService)sut).StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        queue.RequestImmediateRebuildCalled.ShouldBeFalse();
    }

    private sealed class SpyWorkerImageRebuildQueue : IWorkerImageRebuildQueue
    {
        public bool RequestImmediateRebuildCalled { get; private set; }

        public event Action? ImmediateRebuildRequested;

        public void RequestImmediateRebuild()
        {
            RequestImmediateRebuildCalled = true;
            ImmediateRebuildRequested?.Invoke();
            TryEnqueue();
        }

        public bool TryEnqueue() => true;

        public async IAsyncEnumerable<bool> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class NullImageOperations : Docker.DotNet.IImageOperations
    {
        public Task BuildImageFromDockerfileAsync(
            Docker.DotNet.Models.ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<Docker.DotNet.Models.AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<Docker.DotNet.Models.JSONMessage> progress,
            CancellationToken cancellationToken) => Task.CompletedTask;

#pragma warning disable CS0618 // Required for interface compliance
        public Task<Stream> BuildImageFromDockerfileAsync(
            Stream contents,
            Docker.DotNet.Models.ImageBuildParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
#pragma warning restore CS0618

        public Task<IList<Docker.DotNet.Models.ImagesListResponse>> ListImagesAsync(Docker.DotNet.Models.ImagesListParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<Docker.DotNet.Models.ImagesListResponse>>([]);
        public Task<Docker.DotNet.Models.ImageInspectResponse> InspectImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult(new Docker.DotNet.Models.ImageInspectResponse());
        public Task<IList<IDictionary<string, string>>> DeleteImageAsync(string name, Docker.DotNet.Models.ImageDeleteParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<IDictionary<string, string>>>([]);
        public Task<IList<Docker.DotNet.Models.ImageSearchResponse>> SearchImagesAsync(Docker.DotNet.Models.ImagesSearchParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<Docker.DotNet.Models.ImageSearchResponse>>([]);
        public Task CreateImageAsync(Docker.DotNet.Models.ImagesCreateParameters parameters, Docker.DotNet.Models.AuthConfig authConfig, IProgress<Docker.DotNet.Models.JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(Docker.DotNet.Models.ImagesCreateParameters parameters, Docker.DotNet.Models.AuthConfig authConfig, IDictionary<string, string> headers, IProgress<Docker.DotNet.Models.JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(Docker.DotNet.Models.ImagesCreateParameters parameters, Stream imageStream, Docker.DotNet.Models.AuthConfig authConfig, IProgress<Docker.DotNet.Models.JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(Docker.DotNet.Models.ImagesCreateParameters parameters, Stream imageStream, Docker.DotNet.Models.AuthConfig authConfig, IDictionary<string, string> headers, IProgress<Docker.DotNet.Models.JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadImageAsync(Docker.DotNet.Models.ImageLoadParameters parameters, Stream imageStream, IProgress<Docker.DotNet.Models.JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Stream> SaveImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task<Stream> SaveImagesAsync(string[] names, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task TagImageAsync(string name, Docker.DotNet.Models.ImageTagParameters parameters, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PushImageAsync(string name, Docker.DotNet.Models.ImagePushParameters parameters, Docker.DotNet.Models.AuthConfig authConfig, IProgress<Docker.DotNet.Models.JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Docker.DotNet.Models.ImagesPruneResponse> PruneImagesAsync(Docker.DotNet.Models.ImagesPruneParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new Docker.DotNet.Models.ImagesPruneResponse());
        public Task<Docker.DotNet.Models.CommitContainerChangesResponse> CommitContainerChangesAsync(Docker.DotNet.Models.CommitContainerChangesParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new Docker.DotNet.Models.CommitContainerChangesResponse());
        public Task<IList<Docker.DotNet.Models.ImageHistoryResponse>> GetImageHistoryAsync(string name, CancellationToken cancellationToken) => Task.FromResult<IList<Docker.DotNet.Models.ImageHistoryResponse>>([]);
    }

    private sealed class NullIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Foundry.WebApi";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
    }
}
