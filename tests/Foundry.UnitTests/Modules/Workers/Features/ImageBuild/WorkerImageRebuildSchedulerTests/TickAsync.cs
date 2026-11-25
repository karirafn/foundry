using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildServiceTests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildSchedulerTests;

public sealed class TickAsync
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WhenTheLastSuccessfulBuildIsExactly24HoursOld_RequestsAScheduledRebuild()
    {
        // Arrange
        WorkerImageRebuildQueue queue = new();
        StubGlobalSettingsQueries settings = new(lastImageBuiltAt: Now.AddHours(-24));

        // Act
        await RunTickAsync(queue, settings);

        // Assert
        (await ReadPendingAsync(queue)).ShouldBe(ImageRebuildOrigin.Scheduled);
    }

    [Fact]
    public async Task WhenTheLastSuccessfulBuildIsOlderThan24Hours_RequestsAScheduledRebuild()
    {
        // Arrange
        WorkerImageRebuildQueue queue = new();
        StubGlobalSettingsQueries settings = new(lastImageBuiltAt: Now.AddDays(-3));

        // Act
        await RunTickAsync(queue, settings);

        // Assert
        (await ReadPendingAsync(queue)).ShouldBe(ImageRebuildOrigin.Scheduled);
    }

    [Fact]
    public async Task WhenTheLastSuccessfulBuildIsYoungerThan24Hours_RequestsNothing()
    {
        // Arrange
        WorkerImageRebuildQueue queue = new();
        StubGlobalSettingsQueries settings = new(lastImageBuiltAt: Now.AddHours(-24).AddSeconds(1));

        // Act
        await RunTickAsync(queue, settings);

        // Assert
        queue.TryEnqueue(ImageRebuildOrigin.Requested).ShouldBeTrue("nothing was pending");
    }

    [Fact]
    public async Task WhenNoSuccessfulBuildIsRecorded_RequestsNothing()
    {
        // Arrange
        WorkerImageRebuildQueue queue = new();
        StubGlobalSettingsQueries settings = new(lastImageBuiltAt: null);

        // Act
        await RunTickAsync(queue, settings);

        // Assert
        queue.TryEnqueue(ImageRebuildOrigin.Requested).ShouldBeTrue("nothing was pending");
    }

    [Fact]
    public async Task WhenBuildingIsDisabled_RequestsNothing()
    {
        // Arrange
        WorkerImageRebuildQueue queue = new();
        StubGlobalSettingsQueries settings = new(buildEnabled: false, lastImageBuiltAt: Now.AddDays(-2));

        // Act
        await RunTickAsync(queue, settings);

        // Assert
        queue.TryEnqueue(ImageRebuildOrigin.Requested).ShouldBeTrue("nothing was pending");
    }

    [Fact]
    public async Task WhenABuildIsRunning_RequestsNothing()
    {
        // Arrange
        WorkerImageRebuildQueue queue = new();
        StubGlobalSettingsQueries settings = new(
            imageBuildStatus: ImageBuildStatus.Building,
            lastImageBuiltAt: Now.AddDays(-2));

        // Act
        await RunTickAsync(queue, settings);

        // Assert
        queue.TryEnqueue(ImageRebuildOrigin.Requested).ShouldBeTrue("nothing was pending");
    }

    [Fact]
    public async Task WhenAFailedScheduledRebuildAwaitsItsRetry_RequestsNothing()
    {
        // Arrange
        WorkerImageRebuildQueue queue = new();
        StubGlobalSettingsQueries settings = new(
            imageBuildStatus: ImageBuildStatus.Idle,
            lastImageBuiltAt: Now.AddDays(-2),
            nextRetryAt: Now.AddMinutes(5));

        // Act
        await RunTickAsync(queue, settings);

        // Assert
        queue.TryEnqueue(ImageRebuildOrigin.Requested).ShouldBeTrue("nothing was pending");
    }

    [Fact]
    public async Task WhenAFailedRequestedRebuildAwaitsItsRetry_RequestsNothing()
    {
        // Arrange
        WorkerImageRebuildQueue queue = new();
        StubGlobalSettingsQueries settings = new(
            imageBuildStatus: ImageBuildStatus.Failed,
            lastImageBuiltAt: Now.AddDays(-2),
            nextRetryAt: Now.AddMinutes(5));

        // Act
        await RunTickAsync(queue, settings);

        // Assert
        queue.TryEnqueue(ImageRebuildOrigin.Requested).ShouldBeTrue("nothing was pending");
    }

    [Fact]
    public async Task WhenARequestedRebuildIsAlreadyQueued_MergesIntoItWithoutASecondBuild()
    {
        // Arrange
        WorkerImageRebuildQueue queue = new();
        queue.TryEnqueue(ImageRebuildOrigin.Requested);
        StubGlobalSettingsQueries settings = new(lastImageBuiltAt: Now.AddDays(-2));

        // Act
        await RunTickAsync(queue, settings);

        // Assert
        (await ReadPendingAsync(queue)).ShouldBe(ImageRebuildOrigin.Requested);
        queue.TryEnqueue(ImageRebuildOrigin.Requested).ShouldBeTrue("the scheduled request merged and left nothing pending");
    }

    private static async Task RunTickAsync(WorkerImageRebuildQueue queue, StubGlobalSettingsQueries settings)
    {
        ServiceCollection services = new();
        services.AddSingleton<IGlobalSettingsQueries>(settings);
        await using ServiceProvider provider = services.BuildServiceProvider();

        WorkerImageRebuildScheduler sut = new(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(Now),
            NullLogger<WorkerImageRebuildScheduler>.Instance);

        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<ImageRebuildOrigin> ReadPendingAsync(WorkerImageRebuildQueue queue)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        return await queue.ReadAllAsync(timeout.Token).FirstAsync(timeout.Token);
    }
}
