using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Login;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class SeedOnboardingAsync
{
    private const string ConfigPath = "/home/node/.claude/.claude.json";

    private static WorkerOptions DefaultOptions() => new()
    {
        Image = "test-image:latest",
        MemoryLimitMb = 512,
        CpuLimit = 1.5,
        PidsLimit = 256,
    };

    private static DockerWorkerOrchestrator BuildSut(
        RecordingContainerOperations containerOps,
        RecordingExecOperations execOps) =>
        new(containerOps, new NullVolumeOperations(), execOps, Options.Create(DefaultOptions()));

    [Fact]
    public async Task WhenNoPriorConfig_ReadExecCommandIsCat()
    {
        // Arrange
        RecordingExecOperations execOps = new(readStdout: "");
        RecordingContainerOperations containerOps = new();
        DockerWorkerOrchestrator sut = BuildSut(containerOps, execOps);

        // Act
        await sut.SeedOnboardingAsync(CancellationToken.None);

        // Assert — first exec is the cat read
        execOps.AllCreateParameters.ShouldNotBeEmpty();
        ContainerExecCreateParameters readParams = execOps.AllCreateParameters[0];
        readParams.Cmd[0].ShouldBe("cat");
        readParams.Cmd[1].ShouldBe(ConfigPath);
    }

    [Fact]
    public async Task WhenNoPriorConfig_WriteExecCommandIsPrintf()
    {
        // Arrange
        RecordingExecOperations execOps = new(readStdout: "");
        RecordingContainerOperations containerOps = new();
        DockerWorkerOrchestrator sut = BuildSut(containerOps, execOps);

        // Act
        await sut.SeedOnboardingAsync(CancellationToken.None);

        // Assert — second exec is the printf write (runs via sh -c "printf '%s' ...")
        execOps.AllCreateParameters.Count.ShouldBeGreaterThan(1);
        ContainerExecCreateParameters writeParams = execOps.AllCreateParameters[1];
        IList<string> writeCmd = writeParams.Cmd;
        writeCmd[0].ShouldBe("sh");
        writeCmd[1].ShouldBe("-c");
        string shellArg = writeCmd[2];
        shellArg.ShouldContain("printf");
        shellArg.ShouldContain(ConfigPath);
    }

    [Fact]
    public async Task WhenNoPriorConfig_WrittenContentContainsMergedOnboardingFlags()
    {
        // Arrange
        RecordingExecOperations execOps = new(readStdout: "");
        RecordingContainerOperations containerOps = new();
        DockerWorkerOrchestrator sut = BuildSut(containerOps, execOps);

        // Act
        await sut.SeedOnboardingAsync(CancellationToken.None);

        // Assert — the J env variable contains OnboardingSeed.Merge output
        ContainerExecCreateParameters writeParams = execOps.AllCreateParameters[1];
        string? jEnv = writeParams.Env.FirstOrDefault(e => e.StartsWith("J=", StringComparison.Ordinal));
        jEnv.ShouldNotBeNull();
        string writtenJson = jEnv["J=".Length..];
        string expectedJson = OnboardingSeed.Merge(null, OnboardingSeed.DefaultWorkDir);
        writtenJson.ShouldBe(expectedJson);
    }

    [Fact]
    public async Task WhenExistingConfigPresent_MergesWithExistingContent()
    {
        // Arrange
        string existingJson = """{"oauthAccount":{"accountUuid":"abc","emailAddress":"user@example.com"}}""";
        RecordingExecOperations execOps = new(readStdout: existingJson);
        RecordingContainerOperations containerOps = new();
        DockerWorkerOrchestrator sut = BuildSut(containerOps, execOps);

        // Act
        await sut.SeedOnboardingAsync(CancellationToken.None);

        // Assert — the written JSON preserves the existing oauthAccount
        ContainerExecCreateParameters writeParams = execOps.AllCreateParameters[1];
        string? jEnv = writeParams.Env.FirstOrDefault(e => e.StartsWith("J=", StringComparison.Ordinal));
        jEnv.ShouldNotBeNull();
        string writtenJson = jEnv["J=".Length..];
        writtenJson.ShouldContain("oauthAccount");
        writtenJson.ShouldContain("hasCompletedOnboarding");
    }

    [Fact]
    public async Task WhenComplete_StopsAndRemovesHelperContainer()
    {
        // Arrange
        RecordingExecOperations execOps = new(readStdout: "");
        RecordingContainerOperations containerOps = new();
        DockerWorkerOrchestrator sut = BuildSut(containerOps, execOps);

        // Act
        await sut.SeedOnboardingAsync(CancellationToken.None);

        // Assert — the helper container was stopped and removed
        containerOps.StopCalled.ShouldBeTrue();
        containerOps.RemoveCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenStarted_MountsCredentialVolumeReadWrite()
    {
        // Arrange
        RecordingExecOperations execOps = new(readStdout: "");
        RecordingContainerOperations containerOps = new();
        DockerWorkerOrchestrator sut = BuildSut(containerOps, execOps);

        // Act
        await sut.SeedOnboardingAsync(CancellationToken.None);

        // Assert
        CreateContainerParameters captured = containerOps.LastCreateParameters.ShouldNotBeNull();
        IList<Mount> mounts = captured.HostConfig.Mounts.ShouldNotBeNull();
        mounts.ShouldContain(m =>
            m.Type == "volume"
            && m.Source == WorkerVolumeNames.CredentialVolumeName
            && m.Target == WorkerVolumeNames.ClaudeConfigContainerPath
            && !m.ReadOnly);
    }

    /// <summary>
    /// Records all exec calls in order. Returns <paramref name="readStdout"/> for the first
    /// exec (the cat read) and empty for subsequent execs (the write).
    /// </summary>
    private sealed class RecordingExecOperations(string readStdout) : IExecOperations
    {
        private int _callCount;

        public List<ContainerExecCreateParameters> AllCreateParameters { get; } = [];

        public Task<ContainerExecCreateResponse> ExecCreateContainerAsync(
            string id,
            ContainerExecCreateParameters parameters,
            CancellationToken cancellationToken)
        {
            AllCreateParameters.Add(parameters);
            _callCount++;
            return Task.FromResult(new ContainerExecCreateResponse { ID = $"exec-{_callCount}" });
        }

        public Task<MultiplexedStream> StartAndAttachContainerExecAsync(
            string id,
            bool tty,
            CancellationToken cancellationToken)
        {
            // First attach is the cat read — return the configured stdout.
            // Subsequent attaches (write) return empty.
            bool isRead = AllCreateParameters.Count == 1
                || (AllCreateParameters.Count > 0 && AllCreateParameters[^1].Cmd[0] == "cat");
            byte[] bytes = isRead
                ? System.Text.Encoding.UTF8.GetBytes(readStdout)
                : [];
            return Task.FromResult(new MultiplexedStream(new MemoryStream(bytes), tty));
        }

        public Task<ContainerExecInspectResponse> InspectContainerExecAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult(new ContainerExecInspectResponse { ExecID = id, Running = false });

        public Task ResizeContainerExecTtyAsync(string id, ContainerResizeParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StartContainerExecAsync(string id, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<MultiplexedStream> StartWithConfigContainerExecAsync(string id, ContainerExecStartParameters eConfig, CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(new MemoryStream(), false));
    }

    private sealed class RecordingContainerOperations : IContainerOperations
    {
        private const string HelperId = "helper-container-id";

        public bool StopCalled { get; private set; }
        public bool RemoveCalled { get; private set; }
        public CreateContainerParameters? LastCreateParameters { get; private set; }

        public Task<CreateContainerResponse> CreateContainerAsync(
            CreateContainerParameters parameters,
            CancellationToken cancellationToken)
        {
            LastCreateParameters = parameters;
            return Task.FromResult(new CreateContainerResponse { ID = HelperId });
        }

        public Task<bool> StartContainerAsync(string id, ContainerStartParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<bool> StopContainerAsync(string id, ContainerStopParameters parameters, CancellationToken cancellationToken)
        {
            StopCalled = true;
            return Task.FromResult(true);
        }

        public Task RemoveContainerAsync(string id, ContainerRemoveParameters parameters, CancellationToken cancellationToken)
        {
            RemoveCalled = true;
            return Task.CompletedTask;
        }

        public Task<ContainerInspectResponse> InspectContainerAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult(new ContainerInspectResponse());

        public Task<IList<ContainerListResponse>> ListContainersAsync(ContainersListParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<IList<ContainerListResponse>>([]);

        public Task<MultiplexedStream> GetContainerLogsAsync(string id, bool tty, ContainerLogsParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(Stream.Null, false));

        public Task<MultiplexedStream> AttachContainerAsync(string id, bool tty, ContainerAttachParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new MultiplexedStream(Stream.Null, false));

        public Task<Stream> ExportContainerAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task ExtractArchiveToContainerAsync(string id, ContainerPathStatParameters parameters, Stream stream, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<GetArchiveFromContainerResponse> GetArchiveFromContainerAsync(string id, GetArchiveFromContainerParameters parameters, bool statOnly, CancellationToken cancellationToken)
            => Task.FromResult(new GetArchiveFromContainerResponse());

        public Task<Stream> GetContainerLogsAsync(string id, ContainerLogsParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task GetContainerLogsAsync(string id, ContainerLogsParameters parameters, CancellationToken cancellationToken, IProgress<string> progress)
            => Task.CompletedTask;

        public Task<Stream> GetContainerStatsAsync(string id, ContainerStatsParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task GetContainerStatsAsync(string id, ContainerStatsParameters parameters, IProgress<ContainerStatsResponse> progress, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IList<ContainerFileSystemChangeResponse>> InspectChangesAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult<IList<ContainerFileSystemChangeResponse>>([]);

        public Task KillContainerAsync(string id, ContainerKillParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainerProcessesResponse> ListProcessesAsync(string id, ContainerListProcessesParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new ContainerProcessesResponse());

        public Task PauseContainerAsync(string id, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainersPruneResponse> PruneContainersAsync(ContainersPruneParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new ContainersPruneResponse());

        public Task RenameContainerAsync(string id, ContainerRenameParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ResizeContainerTtyAsync(string id, ContainerResizeParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RestartContainerAsync(string id, ContainerRestartParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UnpauseContainerAsync(string id, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<ContainerUpdateResponse> UpdateContainerAsync(string id, ContainerUpdateParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new ContainerUpdateResponse());

        public Task<ContainerWaitResponse> WaitContainerAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult(new ContainerWaitResponse());
    }

    private sealed class NullVolumeOperations : IVolumeOperations
    {
        public Task<VolumeResponse> CreateAsync(VolumesCreateParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new VolumeResponse { Name = parameters.Name });

        public Task<VolumeResponse> InspectAsync(string name, CancellationToken cancellationToken)
            => Task.FromResult(new VolumeResponse { Name = name });

        public Task<VolumesListResponse> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult(new VolumesListResponse());

        public Task<VolumesListResponse> ListAsync(VolumesListParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new VolumesListResponse());

        public Task<VolumesPruneResponse> PruneAsync(VolumesPruneParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(new VolumesPruneResponse());

        public Task RemoveAsync(string name, bool? force, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
