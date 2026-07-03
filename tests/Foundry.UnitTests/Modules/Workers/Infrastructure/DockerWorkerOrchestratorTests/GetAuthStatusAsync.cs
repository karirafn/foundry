using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class GetAuthStatusAsync
{
    private static WorkerOptions DefaultOptions() => new()
    {
        Image = "test-image:latest",
        MemoryLimitMb = 512,
        CpuLimit = 1.5,
        PidsLimit = 256,
    };

    private static DockerWorkerOrchestrator BuildSut(FakeExecOperations execOps) =>
        new(
            new NullContainerOperations(),
            new NullVolumeOperations(),
            execOps,
            Options.Create(DefaultOptions()));

    private static string ValidAuthStatusJson(
        string email = "user@example.com",
        string orgName = "My Org",
        string subscriptionType = "pro") =>
        $$"""{"loggedIn":true,"email":"{{email}}","orgName":"{{orgName}}","subscriptionType":"{{subscriptionType}}"}""";

    [Fact]
    public async Task WhenLoggedIn_ExecTargetsCorrectContainer()
    {
        // Arrange
        FakeExecOperations execOps = new(ValidAuthStatusJson());
        DockerWorkerOrchestrator sut = BuildSut(execOps);

        // Act
        await sut.GetAuthStatusAsync("target-container", CancellationToken.None);

        // Assert
        execOps.LastContainerId.ShouldBe("target-container");
    }

    [Fact]
    public async Task WhenLoggedIn_ExecUsesClaudeAuthStatusJsonCommand()
    {
        // Arrange
        FakeExecOperations execOps = new(ValidAuthStatusJson());
        DockerWorkerOrchestrator sut = BuildSut(execOps);

        // Act
        await sut.GetAuthStatusAsync("container-id", CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = execOps.LastCreateParameters.ShouldNotBeNull();
        captured.Cmd.ShouldBe(["claude", "auth", "status", "--json"]);
    }

    [Fact]
    public async Task WhenLoggedIn_ExecAttachesStdout()
    {
        // Arrange
        FakeExecOperations execOps = new(ValidAuthStatusJson());
        DockerWorkerOrchestrator sut = BuildSut(execOps);

        // Act
        await sut.GetAuthStatusAsync("container-id", CancellationToken.None);

        // Assert
        ContainerExecCreateParameters captured = execOps.LastCreateParameters.ShouldNotBeNull();
        captured.AttachStdout.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenLoggedIn_ReturnsAccountIdentityWithParsedFields()
    {
        // Arrange
        FakeExecOperations execOps = new(ValidAuthStatusJson("alice@acme.com", "Acme Corp", "max"));
        DockerWorkerOrchestrator sut = BuildSut(execOps);

        // Act
        Result<AccountIdentity> result = await sut.GetAuthStatusAsync(
            "container-id",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        AccountIdentity identity = result.ShouldBeOfType<Result<AccountIdentity>.Success>().Value;
        identity.ShouldSatisfyAllConditions(
            () => identity.Email.ShouldBe("alice@acme.com"),
            () => identity.OrgName.ShouldBe("Acme Corp"),
            () => identity.SubscriptionType.ShouldBe("max"));
    }

    [Fact]
    public async Task WhenNotLoggedIn_ReturnsFailure()
    {
        // Arrange
        FakeExecOperations execOps = new("""{"loggedIn":false}""");
        DockerWorkerOrchestrator sut = BuildSut(execOps);

        // Act
        Result<AccountIdentity> result = await sut.GetAuthStatusAsync(
            "container-id",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ShouldBeOfType<Result<AccountIdentity>.Failure>().Error.Code.ShouldBe("AccountIdentity.NotLoggedIn");
    }

    [Fact]
    public async Task WhenOutputExceedsCap_DoesNotThrowOrConsumeUnboundedMemory()
    {
        // Arrange — produce output well beyond the 16 KB cap (20 KB of padding)
        string oversizedOutput = new('x', 20_480);
        FakeExecOperations execOps = new(oversizedOutput);
        DockerWorkerOrchestrator sut = BuildSut(execOps);

        // Act — must not throw an OOM or any other exception; oversized output is truncated
        Result<AccountIdentity> result = await sut.GetAuthStatusAsync(
            "container-id",
            CancellationToken.None);

        // Assert — parse fails gracefully (truncated data is not valid JSON); no crash
        result.IsSuccess.ShouldBeFalse();
    }

    private sealed class NullContainerOperations : IContainerOperations
    {
        public Task<CreateContainerResponse> CreateContainerAsync(
            CreateContainerParameters parameters,
            CancellationToken cancellationToken)
            => Task.FromResult(new CreateContainerResponse { ID = "null-container" });

        public Task<bool> StartContainerAsync(string id, ContainerStartParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<bool> StopContainerAsync(string id, ContainerStopParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task RemoveContainerAsync(string id, ContainerRemoveParameters parameters, CancellationToken cancellationToken)
            => Task.CompletedTask;

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
