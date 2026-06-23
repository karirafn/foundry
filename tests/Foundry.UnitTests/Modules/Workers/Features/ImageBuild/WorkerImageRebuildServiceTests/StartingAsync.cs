using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildServiceTests;

public sealed class StartingAsync : IAsyncDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    public StartingAsync()
    {
        _connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    private void SeedGlobalSettings(bool initiallyFailed = false)
    {
        using FoundryDbContext db = CreateDbContext();
        GlobalSettings settings = GlobalSettings.Create();

        if (initiallyFailed)
        {
            settings.FailImageBuild("previous error");
        }

        db.Set<GlobalSettings>().Add(settings);
        db.SaveChanges();
    }

    private WorkerImageRebuildService BuildService(
        SpyWorkerImageRebuildQueue? queue = null,
        bool imageBuildEnabled = true)
    {
        Microsoft.Data.Sqlite.SqliteConnection connection = _connection;

        ServiceCollection services = new();
        services.AddScoped<FoundryDbContext>(_ =>
        {
            DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
                .UseSqlite(connection)
                .Options;
            return new FoundryDbContext(options);
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

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
            new NullNotificationBroadcaster(),
            NullLogger<WorkerImageRebuildService>.Instance);
    }

    [Fact]
    public async Task OnStartup_EnqueuesRebuildRequest()
    {
        // Arrange
        SeedGlobalSettings();
        SpyWorkerImageRebuildQueue queue = new();
        WorkerImageRebuildService sut = BuildService(queue);

        // Act
        await ((IHostedLifecycleService)sut).StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        queue.EnqueueCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task OnStartup_DoesNotTransitionStatusToBuilding()
    {
        // Arrange — Building transition is owned by ProcessRebuildAsync, not StartingAsync
        SeedGlobalSettings();
        WorkerImageRebuildService sut = BuildService();

        // Act
        await ((IHostedLifecycleService)sut).StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        GlobalSettings? settings = await db.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        settings.ShouldNotBeNull();
        settings.ImageBuildState.ShouldNotBeOfType<ImageBuildState.Building>();
    }

    [Fact]
    public async Task WhenGlobalSettingsMissing_DoesNotThrow()
    {
        // Arrange — no GlobalSettings seeded
        WorkerImageRebuildService sut = BuildService();

        // Act / Assert — should not throw
        await Should.NotThrowAsync(
            async () => await ((IHostedLifecycleService)sut).StartingAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WhenImageBuildDisabled_DoesNotEnqueueRebuild()
    {
        // Arrange
        SeedGlobalSettings();
        SpyWorkerImageRebuildQueue queue = new();
        WorkerImageRebuildService sut = BuildService(queue, imageBuildEnabled: false);

        // Act
        await ((IHostedLifecycleService)sut).StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        queue.EnqueueCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenImageBuildDisabled_DoesNotSetStatusToBuilding()
    {
        // Arrange
        SeedGlobalSettings();
        WorkerImageRebuildService sut = BuildService(imageBuildEnabled: false);

        // Act
        await ((IHostedLifecycleService)sut).StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        GlobalSettings? settings = await db.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        settings.ShouldNotBeNull();
        settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Idle>();
    }

    private sealed class SpyWorkerImageRebuildQueue : IWorkerImageRebuildQueue
    {
        public bool EnqueueCalled { get; private set; }

        public bool TryEnqueue()
        {
            EnqueueCalled = true;
            return true;
        }

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

    private sealed class NullNotificationBroadcaster : ISystemNotificationBroadcaster
    {
        public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
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
