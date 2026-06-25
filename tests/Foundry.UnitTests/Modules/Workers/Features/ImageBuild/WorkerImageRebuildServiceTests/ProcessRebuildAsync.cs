using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildServiceTests;

public sealed class ProcessRebuildAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public ProcessRebuildAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
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

    private void SeedGlobalSettings(
        WorkerImageConfiguration? config = null,
        bool initiallyFailed = false)
    {
        using FoundryDbContext db = CreateDbContext();
        GlobalSettings settings = GlobalSettings.Create();

        if (config is not null)
        {
            settings.UpdateWorkerImageConfiguration(config);
        }

        if (initiallyFailed)
        {
            settings.FailImageBuild("previous error");
        }

        db.Set<GlobalSettings>().Add(settings);
        db.SaveChanges();
    }

    private WorkerImageRebuildService BuildService(
        IImageOperations imageOperations,
        CapturingNotificationBroadcaster? broadcaster = null,
        string contentRootPath = "",
        string? contextPath = null,
        bool imageBuildEnabled = true)
    {
        SqliteConnection connection = _connection;

        Microsoft.Extensions.DependencyInjection.ServiceCollection services = new();
        services.AddScoped<FoundryDbContext>(_ =>
        {
            DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
                .UseSqlite(connection)
                .Options;
            return new FoundryDbContext(options);
        });
        services.AddScoped<DbContext>(sp =>
            sp.GetRequiredService<FoundryDbContext>());

        Microsoft.Extensions.DependencyInjection.ServiceProvider sp = services.BuildServiceProvider();

        WorkerOptions workerOptions = new()
        {
            Image = "test-image:latest",
            ImageBuild = new ImageBuildOptions
            {
                Enabled = imageBuildEnabled,
                ContextPath = contextPath ?? string.Empty,
            },
        };

        StubHostEnvironment hostEnv = new(contentRootPath);

        return new WorkerImageRebuildService(
            new NullWorkerImageRebuildQueue(),
            sp.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            imageOperations,
            hostEnv,
            Options.Create(workerOptions),
            broadcaster ?? new CapturingNotificationBroadcaster(),
            NullLogger<WorkerImageRebuildService>.Instance);
    }

    [Fact]
    public async Task WhenGlobalSettingsMissing_DoesNotBuildImage()
    {
        // Arrange — no GlobalSettings row seeded
        SpyImageOperations spyImages = new();
        WorkerImageRebuildService sut = BuildService(spyImages);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert
        spyImages.BuildCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenGlobalSettingsMissing_DoesNotBroadcastBuildingNotification()
    {
        // Arrange — no GlobalSettings row seeded; Broadcasting "Building" with no settings
        // would leave a stuck banner since no success/failure broadcast follows the early return.
        CapturingNotificationBroadcaster broadcaster = new();
        WorkerImageRebuildService sut = BuildService(new SpyImageOperations(), broadcaster);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert
        broadcaster.Sent.ShouldNotContain(n =>
            n.Category == WorkerImageRebuildService.ImageBuildCategory
            && n.IsActive
            && n.Message == WorkerImageRebuildService.BuildingMessage);
    }

    [Fact]
    public async Task WhenBuildSucceeds_SetsStatusToIdle()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SeedGlobalSettings();

            WorkerImageRebuildService sut = BuildService(
                new SpyImageOperations(),
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            await using FoundryDbContext db = CreateDbContext();
            GlobalSettings? settings = await db.Set<GlobalSettings>()
                .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
            settings.ShouldNotBeNull();
            settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Idle>();
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceeds_ClearsFailedState()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SeedGlobalSettings(initiallyFailed: true);

            WorkerImageRebuildService sut = BuildService(
                new SpyImageOperations(),
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            await using FoundryDbContext db = CreateDbContext();
            GlobalSettings? settings = await db.Set<GlobalSettings>()
                .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
            settings.ShouldNotBeNull();
            settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Idle>();
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceeds_BroadcastsInactiveNotification()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SeedGlobalSettings();

            CapturingNotificationBroadcaster broadcaster = new();
            WorkerImageRebuildService sut = BuildService(
                new SpyImageOperations(),
                broadcaster,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            broadcaster.Sent.ShouldContain(n =>
                n.Category == WorkerImageRebuildService.ImageBuildCategory && !n.IsActive);
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceeds_PassesBuildArgsFromConfig()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            WorkerImageConfiguration config = new(
                InstallDotnet: true,
                InstallAngular: false,
                InstallGlab: false,
                InstallGh: false,
                InstallChromium: false,
                InstallDocker: false);
            SeedGlobalSettings(config);

            SpyImageOperations spyImages = new();
            WorkerImageRebuildService sut = BuildService(
                spyImages,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            spyImages.LastParameters.ShouldNotBeNull();
            spyImages.LastParameters!.BuildArgs.ShouldContainKey("INSTALL_DOTNET");
            spyImages.LastParameters!.BuildArgs["INSTALL_DOTNET"].ShouldBe("true");
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenDockerReportsError_SetsStatusToFailed()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SeedGlobalSettings();

            ErrorReportingImageOperations errorImages = new("Build failed: layer error");
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            await using FoundryDbContext db = CreateDbContext();
            GlobalSettings? settings = await db.Set<GlobalSettings>()
                .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
            settings.ShouldNotBeNull();
            settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Failed>();
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenDockerReportsError_StoresErrorTailInFailedState()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SeedGlobalSettings();

            const string dockerError = "Build failed: layer error";
            ErrorReportingImageOperations errorImages = new(dockerError);
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            await using FoundryDbContext db = CreateDbContext();
            GlobalSettings? settings = await db.Set<GlobalSettings>()
                .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
            settings.ShouldNotBeNull();
            ImageBuildState.Failed failed = settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Failed>();
            failed.ErrorTail.ShouldBe(dockerError);
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenDockerReportsError_BroadcastsActiveNotificationWithError()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SeedGlobalSettings();

            const string dockerError = "Build failed: layer error";
            CapturingNotificationBroadcaster broadcaster = new();
            ErrorReportingImageOperations errorImages = new(dockerError);
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                broadcaster,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — message format: "Failed|<errorTail>"
            broadcaster.Sent.ShouldContain(n =>
                n.Category == WorkerImageRebuildService.ImageBuildCategory
                && n.IsActive
                && n.Message == $"Failed|{dockerError}");
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenDockerThrowsException_SetsStatusToFailed()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SeedGlobalSettings();

            ThrowingImageOperations throwingImages = new("connection refused");
            WorkerImageRebuildService sut = BuildService(
                throwingImages,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            await using FoundryDbContext db = CreateDbContext();
            GlobalSettings? settings = await db.Set<GlobalSettings>()
                .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
            settings.ShouldNotBeNull();
            settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Failed>();
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenDockerThrowsExceptionWithSecret_RedactsSecretFromPersistedErrorTail()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SeedGlobalSettings();

            const string secretMessage = "pull access denied: https://user:sk-ant-api03-secret@registry.example.com/image";
            ThrowingImageOperations throwingImages = new(secretMessage);
            WorkerImageRebuildService sut = BuildService(
                throwingImages,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — the persisted error tail must not contain the raw secret token
            await using FoundryDbContext db = CreateDbContext();
            GlobalSettings? settings = await db.Set<GlobalSettings>()
                .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
            settings.ShouldNotBeNull();
            ImageBuildState.Failed failed = settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Failed>();
            string errorTail = failed.ErrorTail.ShouldNotBeNull();
            errorTail.ShouldNotContain("sk-ant-api03-secret");
            errorTail.ShouldContain("***");
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenBuildStarts_SetsStatusToBuilding()
    {
        // Arrange — use a blocking spy to capture status mid-build
        string contextDir = CreateTempContextDir();

        try
        {
            SeedGlobalSettings();

            StatusCapturingImageOperations capturingImages = new(CreateDbContext);
            WorkerImageRebuildService sut = BuildService(
                capturingImages,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            capturingImages.CapturedStateDuringBuild.ShouldBeOfType<ImageBuildState.Building>();
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenBuildStarts_BroadcastsBuildingNotificationFirst()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SeedGlobalSettings();

            CapturingNotificationBroadcaster broadcaster = new();
            WorkerImageRebuildService sut = BuildService(
                new SpyImageOperations(),
                broadcaster,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — building notification must be first
            broadcaster.Sent.ShouldNotBeEmpty();
            SystemNotification first = broadcaster.Sent[0];
            first.Category.ShouldBe(WorkerImageRebuildService.ImageBuildCategory);
            first.IsActive.ShouldBeTrue();
            first.Message.ShouldBe(WorkerImageRebuildService.BuildingMessage);
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    // Finding 1: notification protocol format tests

    [Fact]
    public async Task BuildingMessage_HasPipeSeparatedFormat()
    {
        // Act
        string message = WorkerImageRebuildService.BuildingMessage;

        // Assert — Angular parser expects "Status|logTail"
        message.ShouldStartWith("Building|");
    }

    [Fact]
    public async Task WhenDockerReportsError_BroadcastsFailedMessageWithPipeSeparator()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SeedGlobalSettings();

            const string dockerError = "Build failed: layer error";
            CapturingNotificationBroadcaster broadcaster = new();
            ErrorReportingImageOperations errorImages = new(dockerError);
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                broadcaster,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — Angular parser expects "Failed|<errorTail>"
            SystemNotification? failureNotification = broadcaster.Sent
                .FirstOrDefault(n =>
                    n.Category == WorkerImageRebuildService.ImageBuildCategory
                    && n.IsActive
                    && n.Message != WorkerImageRebuildService.BuildingMessage);
            failureNotification.ShouldNotBeNull();
            failureNotification.Message.ShouldStartWith("Failed|");
            failureNotification.Message.ShouldContain(dockerError);
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    // Finding 2: OperationCanceledException propagation test

    [Fact]
    public async Task WhenCancellationRequested_PropagatesOperationCanceledException()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SeedGlobalSettings();

            using CancellationTokenSource cts = new();
            CancellingImageOperations cancellingImages = new(cts);
            WorkerImageRebuildService sut = BuildService(
                cancellingImages,
                contextPath: contextDir);

            // Act
            OperationCanceledException ex = await Should.ThrowAsync<OperationCanceledException>(
                async () =>
                {
                    await cts.CancelAsync();
                    await sut.ProcessRebuildAsync(cts.Token);
                });

            // Assert
            ex.ShouldNotBeNull();
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenCancellationRequested_DoesNotPersistFailedStatus()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SeedGlobalSettings();

            using CancellationTokenSource cts = new();
            CancellingImageOperations cancellingImages = new(cts);
            WorkerImageRebuildService sut = BuildService(
                cancellingImages,
                contextPath: contextDir);

            await cts.CancelAsync();

            // Act — OCE is expected and suppressed here; we verify the DB state
            try
            {
                await sut.ProcessRebuildAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected — cancellation propagated correctly
            }

            // Assert — status should not be Failed (it was set to Building before cancel)
            await using FoundryDbContext db = CreateDbContext();
            GlobalSettings? settings = await db.Set<GlobalSettings>()
                .AsNoTracking()
                .FirstOrDefaultAsync(CancellationToken.None);
            settings.ShouldNotBeNull();
            settings.ImageBuildState.ShouldNotBeOfType<ImageBuildState.Failed>();
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    // Finding 3: Enabled flag tests

    [Fact]
    public async Task WhenImageBuildDisabled_DoesNotBuildImage()
    {
        // Arrange
        SeedGlobalSettings();
        SpyImageOperations spyImages = new();
        WorkerImageRebuildService sut = BuildService(spyImages, imageBuildEnabled: false);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert
        spyImages.BuildCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenImageBuildDisabled_DoesNotSetStatusToBuilding()
    {
        // Arrange
        SeedGlobalSettings();
        WorkerImageRebuildService sut = BuildService(new SpyImageOperations(), imageBuildEnabled: false);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        GlobalSettings? settings = await db.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        settings.ShouldNotBeNull();
        settings.ImageBuildState.ShouldBeOfType<ImageBuildState.Idle>();
    }

    private static string CreateTempContextDir()
    {
        string contextDir = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(contextDir);
        File.WriteAllText(Path.Combine(contextDir, "Dockerfile"), "FROM scratch");
        return contextDir;
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

#pragma warning disable CS0618 // Required for interface compliance
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

    private sealed class ErrorReportingImageOperations(string errorMessage) : IImageOperations
    {
        public Task BuildImageFromDockerfileAsync(
            ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<JSONMessage> progress,
            CancellationToken cancellationToken)
        {
            progress.Report(new JSONMessage
            {
                Error = new JSONError { Message = errorMessage },
            });
            return Task.CompletedTask;
        }

#pragma warning disable CS0618 // Required for interface compliance
        public Task<Stream> BuildImageFromDockerfileAsync(Stream contents, ImageBuildParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
#pragma warning restore CS0618

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

    private sealed class ThrowingImageOperations(string message) : IImageOperations
    {
        public Task BuildImageFromDockerfileAsync(
            ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<JSONMessage> progress,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException(message);

#pragma warning disable CS0618 // Required for interface compliance
        public Task<Stream> BuildImageFromDockerfileAsync(Stream contents, ImageBuildParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
#pragma warning restore CS0618

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

    /// <summary>
    /// Captures the GlobalSettings.ImageBuildState mid-build to verify BeginImageBuild() was called.
    /// </summary>
    private sealed class StatusCapturingImageOperations(Func<FoundryDbContext> dbContextFactory) : IImageOperations
    {
        public ImageBuildState? CapturedStateDuringBuild { get; private set; }

        public async Task BuildImageFromDockerfileAsync(
            ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<JSONMessage> progress,
            CancellationToken cancellationToken)
        {
            await using FoundryDbContext db = dbContextFactory();
            GlobalSettings? settings = await db.Set<GlobalSettings>()
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (settings is not null)
            {
                CapturedStateDuringBuild = settings.ImageBuildState;
            }
        }

#pragma warning disable CS0618 // Required for interface compliance
        public Task<Stream> BuildImageFromDockerfileAsync(Stream contents, ImageBuildParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
#pragma warning restore CS0618

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

    private sealed class CancellingImageOperations(CancellationTokenSource cts) : IImageOperations
    {
        public Task BuildImageFromDockerfileAsync(
            ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<JSONMessage> progress,
            CancellationToken cancellationToken)
            => Task.FromCanceled(cts.Token);

#pragma warning disable CS0618 // Required for interface compliance
        public Task<Stream> BuildImageFromDockerfileAsync(Stream contents, ImageBuildParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
#pragma warning restore CS0618

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

    private sealed class CapturingNotificationBroadcaster : ISystemNotificationBroadcaster
    {
        private readonly List<SystemNotification> _sent = [];

        public IReadOnlyList<SystemNotification> Sent => _sent;

        public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
        {
            _sent.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class NullWorkerImageRebuildQueue : IWorkerImageRebuildQueue
    {
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
