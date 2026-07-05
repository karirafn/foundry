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

    private static DockerWorkerOrchestrator BuildSut(FakeDockerContainerRuntime runtime) =>
        new(runtime, Options.Create(DefaultOptions()));

    [Fact]
    public async Task WhenNoPriorConfig_ReadFilePathIsConfigPath()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithReadFileResult(null);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.SeedOnboardingAsync(CancellationToken.None);

        // Assert — ReadFileAsync was called with the correct config path
        runtime.LastReadFilePath.ShouldBe(ConfigPath);
    }

    [Fact]
    public async Task WhenNoPriorConfig_WriteFilePathIsConfigPath()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithReadFileResult(null);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.SeedOnboardingAsync(CancellationToken.None);

        // Assert — WriteFileAsync was called with the correct config path
        runtime.LastWriteFilePath.ShouldBe(ConfigPath);
    }

    [Fact]
    public async Task WhenNoPriorConfig_WrittenContentContainsMergedOnboardingFlags()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithReadFileResult(null);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.SeedOnboardingAsync(CancellationToken.None);

        // Assert — the written content is OnboardingSeed.Merge output
        string writtenJson = runtime.LastWriteFileContent.ShouldNotBeNull();
        string expectedJson = OnboardingSeed.Merge(null, OnboardingSeed.DefaultWorkDir);
        writtenJson.ShouldBe(expectedJson);
    }

    [Fact]
    public async Task WhenExistingConfigPresent_MergesWithExistingContent()
    {
        // Arrange
        string existingJson = """{"oauthAccount":{"accountUuid":"abc","emailAddress":"user@example.com"}}""";
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithReadFileResult(existingJson);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.SeedOnboardingAsync(CancellationToken.None);

        // Assert — the written JSON preserves the existing oauthAccount
        string writtenJson = runtime.LastWriteFileContent.ShouldNotBeNull();
        writtenJson.ShouldContain("oauthAccount");
        writtenJson.ShouldContain("hasCompletedOnboarding");
    }

    [Fact]
    public async Task WhenComplete_StopsAndRemovesHelperContainer()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithContainerId("helper-container-id")
            .WithReadFileResult(null);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.SeedOnboardingAsync(CancellationToken.None);

        // Assert — the helper container was stopped and removed
        runtime.LastStopContainerId.ShouldBe("helper-container-id");
        runtime.LastRemoveContainerId.ShouldBe("helper-container-id");
    }

    [Fact]
    public async Task WhenStarted_MountsCredentialVolumeReadWrite()
    {
        // Arrange
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithReadFileResult(null);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        await sut.SeedOnboardingAsync(CancellationToken.None);

        // Assert
        CreateContainerParameters captured = runtime.LastCreateAndStartParameters.ShouldNotBeNull();
        IList<Mount> mounts = captured.HostConfig.Mounts.ShouldNotBeNull();
        mounts.ShouldContain(m =>
            m.Type == "volume"
            && m.Source == WorkerVolumeNames.CredentialVolumeName
            && m.Target == WorkerVolumeNames.ClaudeConfigContainerPath
            && !m.ReadOnly);
    }
}
