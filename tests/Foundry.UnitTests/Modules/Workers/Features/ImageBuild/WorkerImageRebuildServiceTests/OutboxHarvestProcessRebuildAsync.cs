using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features.WorkerConfig;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildServiceTests;

/// <summary>
/// Drives <see cref="WorkerImageRebuildService.ProcessRebuildAsync"/> against the real
/// <see cref="FoundryDbContext"/> with the outbox interceptor, then relays the outbox through the
/// Settings handlers, so a dispatch that is never harvested into <c>outbox_messages</c> fails here.
/// </summary>
public sealed class OutboxHarvestProcessRebuildAsync : IAsyncDisposable
{
    private const string ImageTag = "foundry-worker:local";

    private readonly SqliteConnection _connection;
    private readonly ImageBuildToggle _imageBuild = new();
    private readonly ServiceProvider _serviceProvider;
    private readonly string _tempRoot;
    private readonly string _contentRoot;

    public OutboxHarvestProcessRebuildAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _serviceProvider = BuildServiceProvider(_connection, _imageBuild);

        using IServiceScope scope = _serviceProvider.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        dbContext.Database.EnsureCreated();
        dbContext.Set<GlobalSettings>().Add(GlobalSettings.Create());
        dbContext.SaveChanges();

        _tempRoot = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}");
        _contentRoot = Path.Combine(_tempRoot, "src", "WebApi");
        string workersDir = Path.Combine(_tempRoot, "workers");
        Directory.CreateDirectory(_contentRoot);
        Directory.CreateDirectory(workersDir);
        File.WriteAllText(Path.Combine(workersDir, "Dockerfile"), "FROM scratch");
        WriteRoleFixture(workersDir);
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
        Directory.Delete(_tempRoot, true);
    }

    [Fact]
    public async Task WhenBuildStarts_ImageBuildRequestedIsPersistedAndRelaysToBuilding()
    {
        // Arrange — relay the outbox while the build is in progress to observe the persisted status mid-build.
        ImageBuildState? stateDuringBuild = null;
        FakeImageOperations images = new()
        {
            OnBuild = async cancellationToken =>
            {
                stateDuringBuild ??= await RelayAndReadStateAsync(cancellationToken);
            },
        };
        WorkerImageRebuildService sut = BuildService(images, buildEnabled: true);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert
        (await ReadOutboxTypesAsync()).ShouldContain(type => type.Contains(nameof(ImageBuildRequested)));
        stateDuringBuild.ShouldBeOfType<ImageBuildState.Building>();
    }

    [Fact]
    public async Task WhenBuildSucceeds_ImageBuildSucceededIsPersistedAndRelaysToIdleWithLastBuiltAt()
    {
        // Arrange
        WorkerImageRebuildService sut = BuildService(new FakeImageOperations(), buildEnabled: true);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);
        await RelayAsync(TestContext.Current.CancellationToken);

        // Assert
        (await ReadOutboxTypesAsync()).ShouldContain(type => type.Contains(nameof(ImageBuildSucceeded)));
        GlobalSettings settings = await ReadSettingsAsync();
        settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Idle>();
        settings.LastImageBuiltAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenBuildFails_ImageBuildOutcomeFailedIsPersistedAndRelaysToFailed()
    {
        // Arrange
        WorkerImageRebuildService sut = BuildService(
            new FakeImageOperations { BuildError = "boom" },
            buildEnabled: true);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);
        await RelayAsync(TestContext.Current.CancellationToken);

        // Assert
        (await ReadOutboxTypesAsync()).ShouldContain(type => type.Contains(nameof(ImageBuildOutcomeFailed)));
        GlobalSettings settings = await ReadSettingsAsync();
        ImageBuildState.Failed failed = settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Failed>();
        failed.Attempt.ShouldBe(1);
        failed.NextRetryAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenBuildDisabledAndImageExists_ImageBuildSucceededIsPersistedAndRelaysToIdle()
    {
        // Arrange
        WorkerImageRebuildService sut = BuildService(new FakeImageOperations(), buildEnabled: false);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);
        await RelayAsync(TestContext.Current.CancellationToken);

        // Assert
        (await ReadOutboxTypesAsync()).ShouldContain(type => type.Contains(nameof(ImageBuildSucceeded)));
        GlobalSettings settings = await ReadSettingsAsync();
        settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Idle>();
        settings.LastImageBuiltAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenBuildDisabledAndImageMissing_ImageBuildOutcomeFailedIsPersistedAndRelaysToFailed()
    {
        // Arrange
        WorkerImageRebuildService sut = BuildService(
            new FakeImageOperations { ImageMissing = true },
            buildEnabled: false);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);
        await RelayAsync(TestContext.Current.CancellationToken);

        // Assert
        (await ReadOutboxTypesAsync()).ShouldContain(type => type.Contains(nameof(ImageBuildOutcomeFailed)));
        GlobalSettings settings = await ReadSettingsAsync();
        settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Failed>().Attempt.ShouldBe(1);
    }

    private static ServiceProvider BuildServiceProvider(SqliteConnection connection, ImageBuildToggle imageBuild)
    {
        ServiceCollection services = new();

        // The Settings query implementation is not under test; only the outbox harvest and relay are.
        services.AddScoped<IGlobalSettingsQueries>(_ =>
            new StubGlobalSettingsQueries(imageTag: ImageTag, buildEnabled: imageBuild.Enabled));

        services.AddScoped<IntegrationEventCollector>();
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddScoped<IIntegrationEventDispatcher, OutboxIntegrationEventDispatcher>();

        HandlerDedupIdentityRegistry registry = new();
        registry.Register(typeof(ImageBuildRequestedHandler));
        registry.Register(typeof(ImageBuildSucceededHandler));
        registry.Register(typeof(ImageBuildOutcomeFailedHandler));
        services.AddSingleton(registry);
        services.AddScoped<IIntegrationEventProcessor, IntegrationEventProcessor>();
        services.AddScoped<IIntegrationEventHandler<ImageBuildRequested>, ImageBuildRequestedHandler>();
        services.AddScoped<IIntegrationEventHandler<ImageBuildSucceeded>, ImageBuildSucceededHandler>();
        services.AddScoped<IIntegrationEventHandler<ImageBuildOutcomeFailed>, ImageBuildOutcomeFailedHandler>();

        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        return services.BuildServiceProvider();
    }

    private WorkerImageRebuildService BuildService(IImageOperations imageOperations, bool buildEnabled)
    {
        _imageBuild.Enabled = buildEnabled;

        return new WorkerImageRebuildService(
            new NullWorkerImageRebuildQueue(),
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            imageOperations,
            new StubHostEnvironment(_contentRoot),
            new WorkerRoleCatalogStore(),
            NullLogger<WorkerImageRebuildService>.Instance);
    }

    private async Task RelayAsync(CancellationToken cancellationToken)
    {
        OutboxRelayService relay = new(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new OutboxOptions(),
            NullLogger<OutboxRelayService>.Instance);
        await relay.TickForTest(cancellationToken);
    }

    private async Task<ImageBuildState> RelayAndReadStateAsync(CancellationToken cancellationToken)
    {
        await RelayAsync(cancellationToken);
        return (await ReadSettingsAsync()).ImageBuildState;
    }

    private async Task<List<string>> ReadOutboxTypesAsync()
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        return await dbContext.Set<OutboxMessage>()
            .Select(message => message.Type)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task<GlobalSettings> ReadSettingsAsync()
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        return settings.ShouldNotBeNull();
    }

    private static void WriteRoleFixture(string workersDir)
    {
        string implement = Path.Combine(workersDir, "roles", "implement");
        string shared = Path.Combine(workersDir, "roles", "shared");
        Directory.CreateDirectory(Path.Combine(implement, ".claude-plugin"));
        Directory.CreateDirectory(Path.Combine(implement, "prompts"));
        Directory.CreateDirectory(Path.Combine(shared, ".claude-plugin"));
        Directory.CreateDirectory(Path.Combine(shared, "prompts"));
        File.WriteAllText(Path.Combine(implement, ".claude-plugin", "plugin.json"), """{"name":"implement","description":"Implements."}""");
        File.WriteAllText(Path.Combine(implement, "prompts", "system.md"), "You are implementing issue #{issueNumber}.\n\n{issueContent}");
        File.WriteAllText(Path.Combine(implement, "prompts", "task.md"), "Implement issue #{issueNumber}.");
        File.WriteAllText(Path.Combine(shared, ".claude-plugin", "plugin.json"), """{"name":"shared"}""");
        File.WriteAllText(Path.Combine(shared, "prompts", "principles.md"), "Engineering principles.");

        string triage = Path.Combine(workersDir, "roles", "triage");
        Directory.CreateDirectory(Path.Combine(triage, ".claude-plugin"));
        Directory.CreateDirectory(Path.Combine(triage, "prompts"));
        File.WriteAllText(Path.Combine(triage, ".claude-plugin", "plugin.json"), """{"name":"triage","description":"Triages."}""");
        File.WriteAllText(Path.Combine(triage, "prompts", "system.md"), "You are triaging issue #{issueNumber}.\n\n{issueContent}");
        File.WriteAllText(Path.Combine(triage, "prompts", "task.md"), "Triage issue #{issueNumber}.");
    }

    private sealed class ImageBuildToggle
    {
        public bool Enabled { get; set; } = true;
    }

    private sealed class FakeImageOperations : IImageOperations
    {
        public string? BuildError { get; init; }
        public bool ImageMissing { get; init; }
        public Func<CancellationToken, Task>? OnBuild { get; init; }

        public async Task BuildImageFromDockerfileAsync(
            ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<JSONMessage> progress,
            CancellationToken cancellationToken)
        {
            if (OnBuild is not null)
            {
                await OnBuild(cancellationToken);
            }

            if (BuildError is not null)
            {
                progress.Report(new JSONMessage { Error = new JSONError { Message = BuildError } });
            }
        }

        public Task<ImageInspectResponse> InspectImageAsync(string name, CancellationToken cancellationToken) =>
            ImageMissing
                ? throw new DockerImageNotFoundException(System.Net.HttpStatusCode.NotFound, "No such image")
                : Task.FromResult(new ImageInspectResponse { ID = "sha256:abc", Created = DateTime.UtcNow });

#pragma warning disable CS0618 // Required for interface compliance
        public Task<Stream> BuildImageFromDockerfileAsync(Stream contents, ImageBuildParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
#pragma warning restore CS0618

        public Task<IList<ImagesListResponse>> ListImagesAsync(ImagesListParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImagesListResponse>>([]);
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

    private sealed class NullWorkerImageRebuildQueue : IWorkerImageRebuildQueue
    {
        public event Action? ImmediateRebuildRequested;

        public void RequestImmediateRebuild() => ImmediateRebuildRequested?.Invoke();

        public bool TryEnqueue() => false;

        public async IAsyncEnumerable<bool> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Foundry.WebApi";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
    }
}
