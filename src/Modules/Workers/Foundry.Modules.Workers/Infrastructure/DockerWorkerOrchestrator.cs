using System.Runtime.CompilerServices;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Docker;

using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers.Infrastructure;

internal sealed class DockerWorkerOrchestrator(
    IDockerContainerRuntime runtime,
    IOptions<WorkerOptions> optionsAccessor) : IWorkerOrchestrator
{
    private const string WorkerRunLabelKey = "foundry.worker-run-id";
    private const string ManagedLabelKey = "foundry.managed";
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

    internal static Mount MapVolumeMount(VolumeMount mount) =>
        new()
        {
            Type = "volume",
            Source = mount.VolumeName,
            Target = mount.ContainerPath,
            ReadOnly = mount.ReadOnly,
        };

    public async Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
    {
        await runtime.CreateVolumeAsync(
            new VolumesCreateParameters
            {
                Name = CredentialVolume.VolumeName,
                Labels = new Dictionary<string, string>
                {
                    [ManagedLabelKey] = "true",
                },
            },
            cancellationToken);
    }

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
                    Mounts = spec.VolumeMounts.Count > 0
                        ? [.. spec.VolumeMounts.Select(MapVolumeMount)]
                        : null,
                },
            };

            string containerId = await runtime.CreateAndStartAsync(createParams, cancellationToken);
            return Result<ContainerId>.Ok(ContainerId.From(containerId));
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
        await runtime.StopAsync(containerId, 10, cancellationToken);
        await runtime.RemoveAsync(containerId, cancellationToken);
    }

    public async Task<WorkerStatusProbe> GetStatusAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            ContainerInspectResponse response = await runtime.InspectAsync(containerId, cancellationToken);

            DateTimeOffset? finishedAt = null;
            if (!string.IsNullOrEmpty(response.State.FinishedAt)
                && DateTimeOffset.TryParse(response.State.FinishedAt, out DateTimeOffset parsed))
            {
                finishedAt = parsed;
            }

            WorkerStatus status = new(
                response.State.Running,
                response.State.Running ? null : (int)Math.Clamp(response.State.ExitCode, int.MinValue, int.MaxValue),
                finishedAt);

            return new WorkerStatusProbe.Available(status);
        }
        catch (DockerContainerNotFoundException)
        {
            return new WorkerStatusProbe.NotFound();
        }
        catch (Exception ex) when (DockerDaemonConnectivity.IsUnreachable(ex, cancellationToken))
        {
            return new WorkerStatusProbe.Unreachable();
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

        IList<ContainerListResponse> containers = await runtime.ListAsync(parameters, cancellationToken);

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
        await foreach (string line in runtime.StreamLogsAsync(containerId, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            yield return SecretRedactor.Redact(line);
        }
    }

    public async Task<string?> GetLogsAsync(
        string containerId,
        int tailLines,
        CancellationToken cancellationToken)
    {
        string? raw = await runtime.GetLogsAsync(containerId, tailLines, cancellationToken);
        if (raw is null)
        {
            return null;
        }

        string redacted = SecretRedactor.Redact(raw);
        return redacted.Length > ContainerOutputMaxBytes
            ? redacted[^ContainerOutputMaxBytes..]
            : redacted;
    }

    public async Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        await runtime.StopAsync(containerId, 10, cancellationToken);
    }

    public async Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        await runtime.RemoveAsync(containerId, cancellationToken);
    }
}
