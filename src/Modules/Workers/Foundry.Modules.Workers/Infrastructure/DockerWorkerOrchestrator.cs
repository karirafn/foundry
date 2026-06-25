using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;

using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers.Infrastructure;

internal sealed class DockerWorkerOrchestrator(
    IContainerOperations containerOperations,
    IOptions<WorkerOptions> optionsAccessor) : IWorkerOrchestrator
{
    private const string WorkerRunLabelKey = "foundry.worker-run-id";
    private const long BytesPerMegabyte = 1024L * 1024L;
    private const long NanoCpusPerCpu = 1_000_000_000L;
    private const int DockerErrorMessageMaxLength = 500;
    private const int ContainerOutputMaxBytes = 65_536;

    private readonly WorkerOptions _options = optionsAccessor.Value;

    internal static string FormatBind(BindMount mount) =>
        mount.ReadOnly
            ? $"{mount.HostPath}:{mount.ContainerPath}:ro"
            : $"{mount.HostPath}:{mount.ContainerPath}";

    internal static DeviceMapping MapDevice(string devicePath) =>
        new()
        {
            PathOnHost = devicePath,
            PathInContainer = devicePath,
            CgroupPermissions = "rwm",
        };

    public async Task<Result<ContainerId>> StartAsync(
        WorkerContainerSpec spec,
        CancellationToken cancellationToken)
    {
        try
        {
            CreateContainerParameters createParams = new()
            {
                Image = spec.Image,
                Cmd = [.. spec.Command],
                Env = [.. spec.EnvironmentVariables.Select(kv => $"{kv.Key}={kv.Value}")],
                Labels = new Dictionary<string, string>(spec.Labels),
                HostConfig = new HostConfig
                {
                    Binds = [.. spec.BindMounts.Select(FormatBind)],
                    Memory = _options.MemoryLimitMb * BytesPerMegabyte,
                    NanoCPUs = (long)(_options.CpuLimit * NanoCpusPerCpu),
                    PidsLimit = _options.PidsLimit,
                    SecurityOpt = spec.SecurityOptions.Count > 0
                        ? [.. spec.SecurityOptions]
                        : null,
                    Devices = spec.Devices.Count > 0
                        ? [.. spec.Devices.Select(MapDevice)]
                        : null,
                },
            };

            CreateContainerResponse response = await containerOperations.CreateContainerAsync(
                createParams,
                cancellationToken);

            await containerOperations.StartContainerAsync(
                response.ID,
                new ContainerStartParameters(),
                cancellationToken);

            return Result<ContainerId>.Ok(ContainerId.From(response.ID));
        }
        catch (DockerApiException ex)
        {
            string redacted = SecretRedactor.Redact(ex.Message);
            string message = redacted.Length > DockerErrorMessageMaxLength
                ? redacted[..DockerErrorMessageMaxLength]
                : redacted;
            return Result<ContainerId>.Fail(new Error("Docker.StartFailed", message));
        }
    }

    public async Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await containerOperations.StopContainerAsync(
                containerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = 10 },
                cancellationToken);

            await containerOperations.RemoveContainerAsync(
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
            ContainerInspectResponse response = await containerOperations.InspectContainerAsync(
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
                response.State.Running ? null : (int)Math.Clamp(response.State.ExitCode, int.MinValue, int.MaxValue),
                finishedAt);
        }
        catch (DockerContainerNotFoundException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
        CancellationToken cancellationToken)
    {
        ContainersListParameters parameters = new()
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool>
                {
                    [WorkerRunLabelKey] = true,
                },
            },
        };

        IList<ContainerListResponse> containers = await containerOperations.ListContainersAsync(
            parameters,
            cancellationToken);

        List<(ContainerId ContainerId, WorkerRunId WorkerRunId)> results = [];

        foreach (ContainerListResponse container in containers)
        {
            if (!container.Labels.TryGetValue(WorkerRunLabelKey, out string? labelValue))
            {
                continue;
            }

            if (!Guid.TryParse(labelValue, out Guid guid))
            {
                continue;
            }

            results.Add((ContainerId.From(container.ID), WorkerRunId.From(guid)));
        }

        return results;
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

        using MultiplexedStream multiplexedStream = await containerOperations.GetContainerLogsAsync(
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
            yield return SecretRedactor.Redact(line);
        }

        await copyTask;
    }

    public async Task<string?> GetLogsAsync(
        string containerId,
        int tailLines,
        CancellationToken cancellationToken)
    {
        try
        {
            ContainerLogsParameters logsParams = new()
            {
                Follow = false,
                ShowStdout = true,
                ShowStderr = true,
                Timestamps = false,
                Tail = tailLines.ToString(CultureInfo.InvariantCulture),
            };

            using MultiplexedStream multiplexedStream = await containerOperations.GetContainerLogsAsync(
                containerId,
                false,
                logsParams,
                cancellationToken);

            using MemoryStream outputStream = new();
            await multiplexedStream.CopyOutputToAsync(
                Stream.Null,
                outputStream,
                outputStream,
                cancellationToken);

            outputStream.Seek(0, SeekOrigin.Begin);
            using StreamReader reader = new(outputStream);
            string output = await reader.ReadToEndAsync(cancellationToken);
            string redacted = SecretRedactor.Redact(output);

            return redacted.Length > ContainerOutputMaxBytes
                ? redacted[^ContainerOutputMaxBytes..]
                : redacted;
        }
        catch (DockerContainerNotFoundException)
        {
            return null;
        }
    }

    public async Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await containerOperations.StopContainerAsync(
                containerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = 10 },
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            // Container already gone — treat as successful stop.
        }
    }

    public async Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await containerOperations.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            // Container already gone — treat as successful removal.
        }
    }
}
