using System.Runtime.CompilerServices;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Docker;

namespace Foundry.Modules.Credentials.Infrastructure;

/// <summary>
/// Docker implementation of <see cref="ICredentialsOrchestrator"/>: manages
/// the login container lifecycle, OAuth code delivery, credential volume auth
/// status, and onboarding seed operations.
/// </summary>
internal sealed class CredentialsOrchestrator(IDockerContainerRuntime runtime) : ICredentialsOrchestrator
{
    private const string LoginLabelKey = "foundry.login";
    private const string ManagedLabelKey = "foundry.managed";
    private const int DockerErrorMessageMaxLength = 500;
    private const int AuthStatusOutputMaxBytes = 16_384;

    public async Task<Result<string>> StartLoginContainerAsync(
        LoginContainerSpec spec,
        CancellationToken cancellationToken)
    {
        try
        {
            string fifoPath = LoginExecCommand.FifoPath;
            string sleepPidPath = LoginExecCommand.SleepPidPath;
            string bootstrapCmd =
                $"mkfifo {fifoPath}; sleep {spec.TimeoutSeconds} > {fifoPath} & echo $! > {sleepPidPath}; timeout -k 10 {spec.TimeoutSeconds} claude auth login --claudeai < {fifoPath}";

            CreateContainerParameters createParams = new()
            {
                Image = WorkerImageNames.LoginImageName,
                Cmd = ["sh", "-c", bootstrapCmd],
                Env = [$"{CredentialVolumeConstants.ConfigDirEnvVar}={CredentialVolumeConstants.ContainerPath}"],
                Tty = false,
                AttachStdout = true,
                AttachStderr = true,
                WorkingDir = OnboardingSeed.DefaultWorkDir,
                Labels = new Dictionary<string, string>
                {
                    [LoginLabelKey] = "true",
                    [ManagedLabelKey] = "true",
                    [ContainerLabelConstants.TransientLabelKey] = ContainerLabelConstants.TransientLabelValue,
                    [ContainerLabelConstants.RoleLabelKey] = ContainerLabelConstants.RoleLogin,
                },
                HostConfig = new HostConfig
                {
                    AutoRemove = true,
                    Mounts =
                    [
                        new Mount
                        {
                            Type = "volume",
                            Source = CredentialVolumeConstants.VolumeName,
                            Target = CredentialVolumeConstants.ContainerPath,
                            ReadOnly = false,
                        },
                    ],
                },
            };

            string containerId = await runtime.CreateAndStartAsync(createParams, cancellationToken);
            return Result<string>.Ok(containerId);
        }
        catch (DockerApiException ex)
        {
            string redacted = SecretRedactor.Redact(ex.Message);
            string message = redacted.Length > DockerErrorMessageMaxLength
                ? redacted[..DockerErrorMessageMaxLength]
                : redacted;
            return Result<string>.Fail(new Error("Docker.LoginStartFailed", message));
        }
    }

    public async Task DeliverLoginCodeAsync(
        string containerId,
        string code,
        CancellationToken cancellationToken)
    {
        LoginExecCommand cmd = LoginExecCommand.ForCode();

        await runtime.ExecAsync(
            containerId,
            cmd.Argv,
            [$"C={code}"],
            cancellationToken);
    }

    public async Task<Result<AccountIdentity>> GetCredentialVolumeAuthStatusAsync(CancellationToken cancellationToken)
    {
        string helperId = await StartCredentialHelperContainerAsync(readOnly: true, cancellationToken);

        try
        {
            return await GetAuthStatusAsync(helperId, cancellationToken);
        }
        finally
        {
            await StopAndRemoveAsync(helperId, cancellationToken);
        }
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

    public async Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        await runtime.StopAsync(containerId, 10, cancellationToken);
    }

    public async Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        await runtime.RemoveAsync(containerId, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListTransientContainersAsync(CancellationToken cancellationToken)
    {
        ContainersListParameters parameters = new()
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool>
                {
                    [$"{ContainerLabelConstants.TransientLabelKey}={ContainerLabelConstants.TransientLabelValue}"] = true,
                },
            },
        };

        IList<ContainerListResponse> containers = await runtime.ListAsync(parameters, cancellationToken);

        List<string> results = [];

        foreach (ContainerListResponse container in containers)
        {
            results.Add(container.ID);
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> ListExitedTransientContainersAsync(CancellationToken cancellationToken)
    {
        ContainersListParameters parameters = new()
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool>
                {
                    [$"{ContainerLabelConstants.TransientLabelKey}={ContainerLabelConstants.TransientLabelValue}"] = true,
                },
            },
        };

        IList<ContainerListResponse> containers = await runtime.ListAsync(parameters, cancellationToken);

        List<string> results = [];

        foreach (ContainerListResponse container in containers)
        {
            if (container.State != "running")
            {
                results.Add(container.ID);
            }
        }

        return results;
    }

    public async Task SeedOnboardingAsync(CancellationToken cancellationToken)
    {
        string configPath = $"{CredentialVolumeConstants.ContainerPath}/.claude.json";

        string helperId = await StartCredentialHelperContainerAsync(readOnly: false, cancellationToken);

        try
        {
            string? existingJson = await runtime.ReadFileAsync(helperId, configPath, cancellationToken);
            string mergedJson = OnboardingSeed.Merge(existingJson, OnboardingSeed.DefaultWorkDir);
            await runtime.WriteFileAsync(helperId, configPath, mergedJson, cancellationToken);
        }
        finally
        {
            await StopAndRemoveAsync(helperId, cancellationToken);
        }
    }

    public async Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
    {
        await runtime.CreateVolumeAsync(
            new VolumesCreateParameters
            {
                Name = CredentialVolumeConstants.VolumeName,
                Labels = new Dictionary<string, string>
                {
                    [ManagedLabelKey] = "true",
                },
            },
            cancellationToken);
    }

    private async Task<Result<AccountIdentity>> GetAuthStatusAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        string json = await runtime.ExecCaptureStdoutAsync(
            containerId,
            ["claude", "auth", "status", "--json"],
            [],
            AuthStatusOutputMaxBytes,
            cancellationToken);

        return AccountIdentity.Parse(json);
    }

    private async Task<string> StartCredentialHelperContainerAsync(bool readOnly, CancellationToken cancellationToken)
    {
        CreateContainerParameters createParams = new()
        {
            Image = WorkerImageNames.LoginImageName,
            Cmd = ["sh", "-c", "sleep 30"],
            Env = [$"{CredentialVolumeConstants.ConfigDirEnvVar}={CredentialVolumeConstants.ContainerPath}"],
            Labels = new Dictionary<string, string>
            {
                [ContainerLabelConstants.TransientLabelKey] = ContainerLabelConstants.TransientLabelValue,
                [ContainerLabelConstants.RoleLabelKey] = ContainerLabelConstants.RoleCredentialHelper,
            },
            HostConfig = new HostConfig
            {
                AutoRemove = true,
                Mounts =
                [
                    new Mount
                    {
                        Type = "volume",
                        Source = CredentialVolumeConstants.VolumeName,
                        Target = CredentialVolumeConstants.ContainerPath,
                        ReadOnly = readOnly,
                    },
                ],
            },
        };

        return await runtime.CreateAndStartAsync(createParams, cancellationToken);
    }

    private async Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
    {
        await runtime.StopAsync(containerId, 10, cancellationToken);
        await runtime.RemoveAsync(containerId, cancellationToken);
    }
}
