using Foundry.Modules.Credentials.Features.Login;
using Foundry.UnitTests.Fakes.Credentials;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Login.TransientContainerReaperTests;

public sealed class TickAsync
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static TransientContainerReaper BuildSut(FakeCredentialsOrchestrator orchestrator) =>
        new(orchestrator, new FixedTimeProvider(Now), NullLogger<TransientContainerReaper>.Instance);

    [Fact]
    public async Task WhenNoTransientContainersExist_DoesNotStopOrRemoveAnyContainers()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new();
        TransientContainerReaper sut = BuildSut(orchestrator);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert
        orchestrator.StopContainerCallCount.ShouldBe(0);
        orchestrator.RemoveContainerCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenTransientContainersExist_StopsAndRemovesEach()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new();
        orchestrator.WithOrphanedTransientContainers("transient-1", "transient-2");
        TransientContainerReaper sut = BuildSut(orchestrator);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert
        orchestrator.StopContainerCallCount.ShouldBe(2);
        orchestrator.RemoveContainerCallCount.ShouldBe(2);
    }

    [Fact]
    public async Task WhenNonRunningContainerIsYoungerThanThePublisherTimeouts_DoesNotReapIt()
    {
        // Arrange — 16 minutes is past the Publisher's 15-minute prepare and publish timeouts,
        // so the grace period must still cover it.
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithExitedTransientContainer("publisher", Now.AddMinutes(-16));
        TransientContainerReaper sut = BuildSut(orchestrator);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert
        orchestrator.StopContainerCallCount.ShouldBe(0);
        orchestrator.RemoveContainerCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenNonRunningContainerIsOlderThanTheGracePeriod_ReapsIt()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new FakeCredentialsOrchestrator()
            .WithExitedTransientContainer("crash-orphan", Now.AddHours(-2));
        TransientContainerReaper sut = BuildSut(orchestrator);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert
        orchestrator.StopContainerCallCount.ShouldBe(1);
        orchestrator.RemoveContainerCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenRunningTransientContainerExists_DoesNotReapIt()
    {
        // Arrange — simulate the active login container (running transient)
        FakeCredentialsOrchestrator orchestrator = new();
        orchestrator.WithRunningTransientContainers("active-login-container");
        TransientContainerReaper sut = BuildSut(orchestrator);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert
        orchestrator.StopContainerCallCount.ShouldBe(0);
        orchestrator.RemoveContainerCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenTickThrows_DoesNotPropagateException()
    {
        // Arrange — orchestrator throws when listing transient containers
        FakeCredentialsOrchestrator orchestrator = new();
        orchestrator.WithListTransientThrows(new InvalidOperationException("Docker daemon unavailable"));
        TransientContainerReaper sut = BuildSut(orchestrator);

        // Act + Assert — tick body swallows non-cancellation exceptions (host-safe)
        await Should.NotThrowAsync(() => sut.TickForTest(CancellationToken.None));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
