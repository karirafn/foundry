using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Login;
using Foundry.Shared;

using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers.Infrastructure;

internal sealed class DockerWorkerOrchestrator(
    IContainerOperations containerOperations,
    IVolumeOperations volumeOperations,
    IExecOperations execOperations,
    IOptions<WorkerOptions> optionsAccessor) : IWorkerOrchestrator
{
    private const string WorkerRunLabelKey = "foundry.worker-run-id";
    private const string LoginLabelKey = "foundry.login";
    private const string ManagedLabelKey = "foundry.managed";
    private const long BytesPerMegabyte = 1024L * 1024L;
    private const long NanoCpusPerCpu = 1_000_000_000L;
    private const int DockerErrorMessageMaxLength = 500;
    private const int ContainerOutputMaxBytes = 65_536;
    private const int AuthStatusOutputMaxBytes = 16_384;

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
        await volumeOperations.CreateAsync(
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
            Timestamps = true,
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
                Timestamps = true,
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

    public async Task<Result<ContainerId>> StartLoginContainerAsync(
        LoginContainerSpec spec,
        CancellationToken cancellationToken)
    {
        try
        {
            string fifoPath = LoginExecCommand.FifoPath;
            // Bootstrap: create FIFO, start a sleep process that holds the writer end open for the
            // session timeout duration, store its PID so DeliverLoginCodeAsync can kill it after
            // writing the code (giving the CLI EOF on stdin), then exec the login CLI.
            string sleepPidPath = LoginExecCommand.SleepPidPath;
            string bootstrapCmd =
                $"mkfifo {fifoPath}; sleep {spec.TimeoutSeconds} > {fifoPath} & echo $! > {sleepPidPath}; exec claude auth login --claudeai < {fifoPath}";

            CreateContainerParameters createParams = new()
            {
                Image = WorkerImageNames.LoginImageName,
                Cmd = ["sh", "-c", bootstrapCmd],
                Env = [$"{CredentialVolume.ConfigDirEnvVar}={CredentialVolume.ContainerPath}"],
                Tty = false,
                AttachStdout = true,
                AttachStderr = true,
                WorkingDir = OnboardingSeed.DefaultWorkDir,
                Labels = new Dictionary<string, string>
                {
                    [LoginLabelKey] = "true",
                    [ManagedLabelKey] = "true",
                },
                HostConfig = new HostConfig
                {
                    Mounts =
                    [
                        new Mount
                        {
                            Type = "volume",
                            Source = CredentialVolume.VolumeName,
                            Target = CredentialVolume.ContainerPath,
                            ReadOnly = false,
                        },
                    ],
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
            return Result<ContainerId>.Fail(new Error("Docker.LoginStartFailed", message));
        }
    }

    public async Task DeliverLoginCodeAsync(
        string containerId,
        string code,
        CancellationToken cancellationToken)
    {
        LoginExecCommand cmd = LoginExecCommand.ForCode();

        ContainerExecCreateResponse execResponse = await execOperations.ExecCreateContainerAsync(
            containerId,
            new ContainerExecCreateParameters
            {
                Cmd = [.. cmd.Argv],
                // Code is delivered via the C env var — never interpolated into the shell command.
                // Stream-stdin delivery is not viable: Docker.DotNet WriteAsync does not land on
                // the Windows npipe transport (bytes never reach the container).
                Env = [$"C={code}"],
                AttachStdin = false,
                AttachStdout = true,
                AttachStderr = true,
            },
            cancellationToken);

        using MultiplexedStream stream = await execOperations.StartAndAttachContainerExecAsync(
            execResponse.ID,
            false,
            cancellationToken);

        await stream.CopyOutputToAsync(Stream.Null, Stream.Null, Stream.Null, cancellationToken);
    }

    public async Task<Result<AccountIdentity>> GetAuthStatusAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        ContainerExecCreateResponse execResponse = await execOperations.ExecCreateContainerAsync(
            containerId,
            new ContainerExecCreateParameters
            {
                Cmd = ["claude", "auth", "status", "--json"],
                AttachStdout = true,
                AttachStderr = false,
            },
            cancellationToken);

        using MultiplexedStream stream = await execOperations.StartAndAttachContainerExecAsync(
            execResponse.ID,
            false,
            cancellationToken);

        using MemoryStream stdoutStream = new();
        await stream.CopyOutputToAsync(Stream.Null, stdoutStream, Stream.Null, cancellationToken);

        stdoutStream.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(stdoutStream, Encoding.UTF8);

        // Read only up to AuthStatusOutputMaxBytes to prevent unbounded memory consumption
        // from a misbehaving or compromised container writing excessive output.
        char[] buffer = new char[AuthStatusOutputMaxBytes];
        int charsRead = await reader.ReadAsync(buffer, cancellationToken);
        string json = new(buffer, 0, charsRead);

        return AccountIdentity.Parse(json);
    }

    public async Task<IReadOnlyList<ContainerId>> ListLoginContainersByLabelAsync(
        CancellationToken cancellationToken)
    {
        ContainersListParameters parameters = new()
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool>
                {
                    [LoginLabelKey] = true,
                },
            },
        };

        IList<ContainerListResponse> containers = await containerOperations.ListContainersAsync(
            parameters,
            cancellationToken);

        List<ContainerId> results = [];

        foreach (ContainerListResponse container in containers)
        {
            results.Add(ContainerId.From(container.ID));
        }

        return results;
    }

    public async Task<Result<AccountIdentity>> GetCredentialVolumeAuthStatusAsync(CancellationToken cancellationToken)
    {
        string helperId = await StartCredentialHelperContainerAsync(cancellationToken);

        try
        {
            return await GetAuthStatusAsync(helperId, cancellationToken);
        }
        finally
        {
            await StopAndRemoveAsync(helperId, cancellationToken);
        }
    }

    public async Task SeedOnboardingAsync(CancellationToken cancellationToken)
    {
        // Use a short-lived helper container to read, merge, and write .claude.json
        // onto the credential volume without copying the volume to the host.
        string configPath = $"{CredentialVolume.ContainerPath}/.claude.json";

        string helperId = await StartCredentialHelperContainerAsync(cancellationToken);

        try
        {
            // Read existing .claude.json (tolerate absence).
            string? existingJson = await ReadFileFromContainerAsync(helperId, configPath, cancellationToken);

            // Compute the merged JSON in C# — OnboardingSeed.Merge is the single source of truth.
            string mergedJson = OnboardingSeed.Merge(existingJson, OnboardingSeed.DefaultWorkDir);

            // Write the merged content back via printf (code in env J — no shell injection).
            await WriteFileToContainerAsync(helperId, configPath, mergedJson, cancellationToken);
        }
        finally
        {
            await StopAndRemoveAsync(helperId, cancellationToken);
        }
    }

    /// <summary>
    /// Starts a minimal helper container that mounts the credential volume and sleeps long enough
    /// for exec operations. The caller is responsible for stopping and removing it via
    /// <see cref="StopAndRemoveAsync"/> in a <c>finally</c> block.
    /// </summary>
    private async Task<string> StartCredentialHelperContainerAsync(CancellationToken cancellationToken)
    {
        CreateContainerParameters createParams = new()
        {
            Image = WorkerImageNames.LoginImageName,
            Cmd = ["sh", "-c", "sleep 30"],
            Env = [$"{CredentialVolume.ConfigDirEnvVar}={CredentialVolume.ContainerPath}"],
            HostConfig = new HostConfig
            {
                Mounts =
                [
                    new Mount
                    {
                        Type = "volume",
                        Source = CredentialVolume.VolumeName,
                        Target = CredentialVolume.ContainerPath,
                        ReadOnly = false,
                    },
                ],
            },
        };

        CreateContainerResponse response = await containerOperations.CreateContainerAsync(
            createParams,
            cancellationToken);

        await containerOperations.StartContainerAsync(
            response.ID,
            new ContainerStartParameters(),
            cancellationToken);

        return response.ID;
    }

    private async Task<string?> ReadFileFromContainerAsync(
        string containerId,
        string filePath,
        CancellationToken cancellationToken)
    {
        ContainerExecCreateResponse execResponse = await execOperations.ExecCreateContainerAsync(
            containerId,
            new ContainerExecCreateParameters
            {
                Cmd = ["cat", filePath],
                AttachStdout = true,
                AttachStderr = false,
            },
            cancellationToken);

        using MultiplexedStream stream = await execOperations.StartAndAttachContainerExecAsync(
            execResponse.ID,
            false,
            cancellationToken);

        using MemoryStream stdoutStream = new();
        await stream.CopyOutputToAsync(Stream.Null, stdoutStream, Stream.Null, cancellationToken);

        stdoutStream.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(stdoutStream, Encoding.UTF8);
        string content = await reader.ReadToEndAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(content) ? null : content;
    }

    private async Task WriteFileToContainerAsync(
        string containerId,
        string filePath,
        string content,
        CancellationToken cancellationToken)
    {
        // Pass content via env J — never interpolated — to avoid shell injection.
        ContainerExecCreateResponse execResponse = await execOperations.ExecCreateContainerAsync(
            containerId,
            new ContainerExecCreateParameters
            {
                Cmd = ["sh", "-c", $"printf '%s' \"$J\" > {filePath}"],
                Env = [$"J={content}"],
                AttachStdout = false,
                AttachStderr = false,
            },
            cancellationToken);

        using MultiplexedStream stream = await execOperations.StartAndAttachContainerExecAsync(
            execResponse.ID,
            false,
            cancellationToken);

        await stream.CopyOutputToAsync(Stream.Null, Stream.Null, Stream.Null, cancellationToken);
    }
}
