using System.Formats.Tar;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Settings.Domain;

using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers.Features.ImageBuild;

internal sealed class WorkerImageRebuildService(
    IWorkerImageRebuildQueue rebuildQueue,
    IServiceScopeFactory scopeFactory,
    IImageOperations imageOperations,
    IHostEnvironment hostEnvironment,
    IOptions<WorkerOptions> optionsAccessor,
    ISystemNotificationBroadcaster broadcaster,
    ILogger<WorkerImageRebuildService> logger) : BackgroundService
{
    internal const string ImageBuildCategory = "image-build";
    internal const string BuildingMessage = "Worker image is building...";
    private const int LogTailLines = 200;

    private readonly WorkerOptions _options = optionsAccessor.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (bool _ in rebuildQueue.ReadAllAsync(stoppingToken))
        {
            await ProcessRebuildAsync(stoppingToken);
        }
    }

    internal async Task ProcessRebuildAsync(CancellationToken cancellationToken)
    {
        await broadcaster.SendAsync(
            new SystemNotification(ImageBuildCategory, true, BuildingMessage),
            cancellationToken);

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            logger.LogWarning(
                "Worker image rebuild requested but no GlobalSettings row exists; skipping.");
            return;
        }

        settings.BeginImageBuild();
        await dbContext.SaveChangesAsync(cancellationToken);

        string contextPath = ResolveContextPath(_options.ImageBuild.ContextPath);
        IReadOnlyDictionary<string, string> buildArgs = settings.WorkerImageConfiguration.ToBuildArgs();

        logger.LogInformation(
            "Building worker image '{Image}' from context '{ContextPath}'.",
            _options.Image,
            contextPath);

        string? errorTail = null;

        try
        {
            await using MemoryStream tarStream = new();
            await TarFile.CreateFromDirectoryAsync(
                contextPath,
                tarStream,
                includeBaseDirectory: false,
                cancellationToken);
            tarStream.Seek(0, SeekOrigin.Begin);

            ImageBuildParameters buildParameters = new()
            {
                Tags = [_options.Image],
                BuildArgs = new Dictionary<string, string>(buildArgs),
                Dockerfile = "Dockerfile",
            };

            BuildProgress progress = new(logger);

            await imageOperations.BuildImageFromDockerfileAsync(
                buildParameters,
                tarStream,
                authConfigs: null,
                headers: null,
                progress,
                cancellationToken);

            if (progress.HasError)
            {
                errorTail = TruncateTail(progress.LastError);
            }
        }
#pragma warning disable CA1031 // Docker build failures must be surfaced as status notifications, not exceptions, to avoid crashing the BackgroundService consumer loop.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Worker image build failed with an unhandled exception.");
            errorTail = TruncateTail(ex.Message);
        }

        if (errorTail is null)
        {
            settings.CompleteImageBuild();
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Worker image '{Image}' built successfully.", _options.Image);

            await broadcaster.SendAsync(
                new SystemNotification(ImageBuildCategory, false, string.Empty),
                cancellationToken);
        }
        else
        {
            settings.FailImageBuild(errorTail);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogError(
                "Worker image '{Image}' build failed: {Error}",
                _options.Image,
                errorTail);

            await broadcaster.SendAsync(
                new SystemNotification(ImageBuildCategory, true, errorTail),
                cancellationToken);
        }
    }

    private string ResolveContextPath(string configuredContextPath)
    {
        // ContentRootPath is src/Foundry.WebApi/ — solution root is two directories up.
        string solutionRoot = Path.GetFullPath(
            Path.Combine(hostEnvironment.ContentRootPath, "..", ".."));

        return Path.GetFullPath(Path.Combine(solutionRoot, configuredContextPath));
    }

    private static string TruncateTail(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length > LogTailLines * 120
            ? text[^(LogTailLines * 120)..]
            : text;
    }

    private sealed class BuildProgress(ILogger logger) : IProgress<JSONMessage>
    {
        public bool HasError { get; private set; }
        public string? LastError { get; private set; }

        public void Report(JSONMessage message)
        {
            if (message.Error is not null)
            {
                HasError = true;
                LastError = message.Error.Message;
                logger.LogError("Docker build error: {Error}", message.Error.Message);
                return;
            }

            string? text = message.Stream?.TrimEnd('\n', '\r');
            if (!string.IsNullOrWhiteSpace(text))
            {
                logger.LogDebug("Docker build: {Message}", text);
            }
            else if (!string.IsNullOrWhiteSpace(message.Status))
            {
                logger.LogDebug("Docker build: {Status}", message.Status);
            }
        }
    }
}
