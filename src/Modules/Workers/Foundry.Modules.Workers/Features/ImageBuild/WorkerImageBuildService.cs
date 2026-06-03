using System.Formats.Tar;

using Docker.DotNet;
using Docker.DotNet.Models;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers.Features.ImageBuild;

internal sealed class WorkerImageBuildService(
    IImageOperations imageOperations,
    IHostEnvironment hostEnvironment,
    IOptions<WorkerOptions> optionsAccessor,
    ILogger<WorkerImageBuildService> logger) : IHostedLifecycleService
{
    private readonly WorkerOptions _options = optionsAccessor.Value;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!_options.ImageBuild.Enabled)
        {
            logger.LogInformation(
                "Worker image build is disabled. Skipping build of '{Image}'.",
                _options.Image);
            return;
        }

        string contextPath = ResolveContextPath(_options.ImageBuild.ContextPath);

        if (!Directory.Exists(contextPath))
        {
            throw new InvalidOperationException(
                $"Worker image build context directory does not exist: '{contextPath}'. " +
                $"Configure Workers:ImageBuild:ContextPath to point to a valid context directory.");
        }

        logger.LogInformation(
            "Building worker image '{Image}' from context '{ContextPath}'.",
            _options.Image,
            contextPath);

        MemoryStream tarStream = new();
        await TarFile.CreateFromDirectoryAsync(
            contextPath,
            tarStream,
            includeBaseDirectory: false,
            cancellationToken);
        tarStream.Seek(0, SeekOrigin.Begin);

        ImageBuildParameters buildParameters = new()
        {
            Tags = [_options.Image],
            BuildArgs = new Dictionary<string, string>(_options.ImageBuild.BuildArgs),
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
            throw new InvalidOperationException(
                $"Docker image build failed: {progress.LastError}");
        }

        logger.LogInformation("Worker image '{Image}' built successfully.", _options.Image);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private string ResolveContextPath(string configuredContextPath)
    {
        if (Path.IsPathRooted(configuredContextPath))
        {
            return configuredContextPath;
        }

        // ContentRootPath is src/Foundry.WebApi/ — solution root is two directories up.
        string solutionRoot = Path.GetFullPath(
            Path.Combine(hostEnvironment.ContentRootPath, "..", ".."));

        return Path.GetFullPath(Path.Combine(solutionRoot, configuredContextPath));
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
