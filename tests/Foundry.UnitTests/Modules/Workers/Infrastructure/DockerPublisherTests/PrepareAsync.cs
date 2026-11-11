using System.Net;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerPublisherTests;

public sealed class PrepareAsync
{
    private const string DefaultSha = "1111111111111111111111111111111111111111";
    private const string BranchSha = "2222222222222222222222222222222222222222";

    private readonly WorkerRunId _runId = WorkerRunId.New();

    private static DockerPublisher BuildSut(FakeDockerContainerRuntime runtime) =>
        new(runtime, NullLogger<DockerPublisher>.Instance);

    private PrepareRequest Request(BranchName? runBranch = null) =>
        new(
            _runId,
            new Uri("https://github.com/owner/repo.git"),
            "ghp_publisher_token",
            runBranch,
            new WorkerResourceLimitsSnapshot(MemoryLimitMb: 2048, CpuLimit: 1.5, PidsLimit: 512));

    private static FakeDockerContainerRuntime Exiting(long exitCode, string log) =>
        new FakeDockerContainerRuntime()
            .WithContainerId("publisher-1")
            .WithWaitExitCode(exitCode)
            .WithGetAllLogsResult(log);

    [Fact]
    public async Task WhenPrepareSucceeds_ReturnsTheReportedBase()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = Exiting(0, $"2026-11-11T10:00:00.000000000Z FOUNDRY_BASE default_branch=main default_sha={DefaultSha}\n");

        // Act
        PrepareOutcome outcome = await BuildSut(runtime).PrepareAsync(Request(), CancellationToken.None);

        // Assert
        outcome.ShouldBe(new PrepareOutcome.Prepared(new RunBase(CommitSha.From(DefaultSha), RunBranchHead: null)));
    }

    [Fact]
    public async Task WhenPreparingARunBranch_ReturnsItsHeadToo()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = Exiting(0, $"FOUNDRY_BASE default_branch=main default_sha={DefaultSha} branch_sha={BranchSha}\n");

        // Act
        PrepareOutcome outcome = await BuildSut(runtime).PrepareAsync(Request(BranchName.From("feat/1-x")), CancellationToken.None);

        // Assert
        outcome.ShouldBe(new PrepareOutcome.Prepared(new RunBase(CommitSha.From(DefaultSha), CommitSha.From(BranchSha))));
    }

    [Fact]
    public async Task CreatesTheRunsHandOffVolumeLabelledWithTheRunId()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = Exiting(0, $"FOUNDRY_BASE default_sha={DefaultSha}\n");

        // Act
        await BuildSut(runtime).PrepareAsync(Request(), CancellationToken.None);

        // Assert
        VolumesCreateParameters volume = runtime.LastCreateVolumeParameters.ShouldNotBeNull();
        volume.ShouldSatisfyAllConditions(
            () => volume.Name.ShouldBe(WorkerVolumeNames.HandOffVolumeName(_runId)),
            () => volume.Labels["foundry.worker-run-id"].ShouldBe(_runId.Value.ToString()),
            () => volume.Labels["foundry.managed"].ShouldBe("true"));
    }

    [Fact]
    public async Task RunsThePublisherImageInPrepareModeWithTheTokenAndRunBranch()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = Exiting(0, $"FOUNDRY_BASE default_sha={DefaultSha} branch_sha={BranchSha}\n");

        // Act
        await BuildSut(runtime).PrepareAsync(Request(BranchName.From("feat/1-x")), CancellationToken.None);

        // Assert
        CreateContainerParameters create = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        create.ShouldSatisfyAllConditions(
            () => create.Image.ShouldBe(WorkerImageNames.PublisherImageName),
            () => create.Cmd.ShouldBe(["prepare"]),
            () => create.Env.ShouldBe(
                ["CLONE_URL=https://github.com/owner/repo.git", "GIT_PAT=ghp_publisher_token", "RUN_BRANCH=feat/1-x"],
                ignoreOrder: true),
            () => create.Labels["foundry.publisher-run-id"].ShouldBe(_runId.Value.ToString()),
            () => create.Labels.ShouldNotContainKey("foundry.worker-run-id"));
    }

    [Fact]
    public async Task RunsUnderTheWorkerHardeningWithNoCapabilityAddedBack()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = Exiting(0, $"FOUNDRY_BASE default_sha={DefaultSha}\n");

        // Act
        await BuildSut(runtime).PrepareAsync(Request(), CancellationToken.None);

        // Assert
        HostConfig host = runtime.LastCreateAndStartParameters.ShouldNotBeNull().HostConfig;
        host.ShouldSatisfyAllConditions(
            () => host.CapDrop.ShouldBe(["ALL"]),
            () => host.CapAdd.ShouldBeNull(),
            () => host.SecurityOpt.ShouldBe(["no-new-privileges:true"]),
            () => host.ReadonlyRootfs.ShouldBeTrue(),
            () => host.Tmpfs.ShouldBeNull(),
            () => host.Memory.ShouldBe(2048L * 1024 * 1024),
            () => host.PidsLimit.ShouldBe(512));
    }

    [Fact]
    public async Task MountsScratchVolumesAtWorkAndTheTempDirectoriesAndTheHandOffVolume()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = Exiting(0, $"FOUNDRY_BASE default_sha={DefaultSha}\n");

        // Act
        await BuildSut(runtime).PrepareAsync(Request(), CancellationToken.None);

        // Assert
        IList<Mount> mounts = runtime.LastCreateAndStartParameters.ShouldNotBeNull().HostConfig.Mounts.ShouldNotBeNull();
        mounts.Where(m => m.Source is null).Select(m => m.Target).ShouldBe(["/work", "/tmp", "/var/tmp"], ignoreOrder: true);
        Mount handOff = mounts.Where(m => m.Source is not null).ShouldHaveSingleItem();
        handOff.ShouldSatisfyAllConditions(
            () => handOff.Source.ShouldBe(WorkerVolumeNames.HandOffVolumeName(_runId)),
            () => handOff.Target.ShouldBe("/handoff"),
            () => handOff.ReadOnly.ShouldBeFalse());
    }

    [Fact]
    public async Task RemovesThePublisherContainerOnceItsExitIsRead()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = Exiting(0, $"FOUNDRY_BASE default_sha={DefaultSha}\n");

        // Act
        await BuildSut(runtime).PrepareAsync(Request(), CancellationToken.None);

        // Assert
        runtime.CallLog.ShouldBe(["wait:publisher-1", "remove:publisher-1"]);
        runtime.LastGetAllLogsContainerId.ShouldBe("publisher-1");
    }

    [Fact]
    public async Task WhenPrepareFailsWithTheSentinel_ReturnsABootstrapFailure()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = Exiting(
            1,
            "FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=could not reach the repository: fatal: Authentication failed\n");

        // Act
        PrepareOutcome outcome = await BuildSut(runtime).PrepareAsync(Request(), CancellationToken.None);

        // Assert
        outcome.ShouldBe(new PrepareOutcome.Failed(new FailureReason.WorkerBootstrapFailed(
            "FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=could not reach the repository: fatal: Authentication failed")));
        runtime.LastRemoveContainerId.ShouldBe("publisher-1");
    }

    [Fact]
    public async Task WhenDockerRejectsThePublisher_ReturnsAContainerErrorWithoutThrowing()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithCreateAndStartThrowing(new DockerApiException(HttpStatusCode.NotFound, "No such image: foundry-publisher:local"));

        // Act
        PrepareOutcome outcome = await BuildSut(runtime).PrepareAsync(Request(), CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<PrepareOutcome.Failed>().Reason.ShouldBeOfType<FailureReason.ContainerError>()
            .Message.ShouldContain("No such image: foundry-publisher:local");
        runtime.LastRemoveContainerId.ShouldBeNull();
    }
}
