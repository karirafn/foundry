using System.IO.Pipelines;
using System.Runtime.CompilerServices;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.WebApi.Modules.Workers.Features;
using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Workers.Infrastructure;

internal sealed class DockerWorkerOrchestrator(DockerClient dockerClient) : IWorkerOrchestrator
{
    public async Task<Result<string>> StartAsync(
        WorkerContainerSpec spec,
        CancellationToken cancellationToken)
    {
        try
        {
            CreateContainerParameters createParams = new()
            {
                Image = spec.Image,
                Env = [.. spec.EnvironmentVariables.Select(kv => $"{kv.Key}={kv.Value}")],
                Labels = new Dictionary<string, string>(spec.Labels),
                HostConfig = new HostConfig
                {
                    Binds = [.. spec.BindMounts.Select(b => $"{b.HostPath}:{b.ContainerPath}")],
                },
            };

            CreateContainerResponse response = await dockerClient.Containers.CreateContainerAsync(
                createParams,
                cancellationToken);

            await dockerClient.Containers.StartContainerAsync(
                response.ID,
                new ContainerStartParameters(),
                cancellationToken);

            return Result<string>.Ok(response.ID);
        }
        catch (DockerApiException ex)
        {
            return Result<string>.Fail(new Error("Docker.StartFailed", ex.Message));
        }
    }

    public async Task StopAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await dockerClient.Containers.StopContainerAsync(
                containerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = 10 },
                cancellationToken);

            await dockerClient.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            // Container already gone — treat as successful stop.
        }
    }

    public async Task<WorkerStatus?> GetStatusAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            ContainerInspectResponse response = await dockerClient.Containers.InspectContainerAsync(
                containerId,
                cancellationToken);

            DateTimeOffset? finishedAt = null;
            if (!string.IsNullOrEmpty(response.State.FinishedAt)
                && DateTimeOffset.TryParse(response.State.FinishedAt, out DateTimeOffset parsed))
            {
                finishedAt = parsed;
            }

            return new WorkerStatus(
                response.State.Running,
                response.State.Running ? null : (int)response.State.ExitCode,
                finishedAt);
        }
        catch (DockerContainerNotFoundException)
        {
            return null;
        }
    }

    public async IAsyncEnumerable<string> StreamLogsAsync(
        string containerId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ContainerLogsParameters logsParams = new()
        {
            Follow = true,
            ShowStdout = true,
            ShowStderr = true,
            Timestamps = false,
        };

        using MultiplexedStream multiplexedStream = await dockerClient.Containers.GetContainerLogsAsync(
            containerId,
            false,
            logsParams,
            cancellationToken);

        Pipe pipe = new();

        Task copyTask = Task.Run(
            async () =>
            {
                try
                {
                    await multiplexedStream.CopyOutputToAsync(
                        Stream.Null,
                        pipe.Writer.AsStream(),
                        pipe.Writer.AsStream(),
                        cancellationToken);
                }
                finally
                {
                    await pipe.Writer.CompleteAsync();
                }
            },
            cancellationToken);

        using StreamReader reader = new(pipe.Reader.AsStream());

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            yield return line;
        }

        await copyTask;
    }
}
