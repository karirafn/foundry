using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class GetLogsAsync
{
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
    public async Task WhenOutputContainsHttpsUrlWithUserinfo_UserinfoRedacted()
    {
        // Arrange
        string raw = "Cloning into '/workspace'...\nhttps://glpat-MySecretToken@gitlab.example.com/owner/repo.git";
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithGetLogsResult(raw);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        string? result = await sut.GetLogsAsync("container-1", 500, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldNotContain("glpat-MySecretToken");
        result.ShouldContain("https://***@gitlab.example.com");
    }

    [Fact]
    public async Task WhenOutputContainsKnownTokenShape_TokenRedacted()
    {
        // Arrange
        string raw = "error: Authentication failed for token ghp_abc123DefXyz";
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithGetLogsResult(raw);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        string? result = await sut.GetLogsAsync("container-2", 500, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldNotContain("ghp_abc123DefXyz");
        result.ShouldContain("***");
    }

    [Fact]
    public async Task WhenOutputIsClean_PassesThroughUnchanged()
    {
        // Arrange
        string raw = "Cloning into '/workspace'...\nDone.";
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithGetLogsResult(raw);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        string? result = await sut.GetLogsAsync("container-3", 500, CancellationToken.None);

        // Assert
        result.ShouldBe(raw);
    }

    [Fact]
    public async Task WhenSecretNearTruncationBoundary_SecretRedactedBeforeTruncation()
    {
        // Arrange — put secret at position 65500 (within the 65536-byte window)
        // If truncation happened before redaction, the secret could survive.
        string prefix = new('a', 65_500);
        string raw = prefix + " ghp_SecretNearBoundary extra padding to exceed 65536";
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithGetLogsResult(raw);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        string? result = await sut.GetLogsAsync("container-4", 500, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldNotContain("ghp_SecretNearBoundary");
    }

    [Fact]
    public async Task WhenSecretPrefixStraddlesByteWindow_ValueIsRedacted()
    {
        // Arrange — token sits at the very start of the output, followed by >65_519 bytes
        // of padding. The old code seeks to (total_bytes - 65_536), which puts the read
        // cursor at byte 1 — cutting off the 'g' in "ghp_" so the tail string begins with
        // "hp_StraddleToken..." and the ghp_\S+ regex never matches, leaking the value.
        // The fix must redact the FULL buffer first, then keep the tail.
        string suffix = new('b', 65_520);                    // 65_520 padding bytes after token
        string raw = "ghp_StraddleToken" + suffix;           // total = 65_537 chars; window start = byte 1
        FakeDockerContainerRuntime runtime = new FakeDockerContainerRuntime()
            .WithGetLogsResult(raw);
        DockerWorkerOrchestrator sut = BuildSut(runtime);

        // Act
        string? result = await sut.GetLogsAsync("container-5", 500, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldNotContain("StraddleToken");
    }
}
