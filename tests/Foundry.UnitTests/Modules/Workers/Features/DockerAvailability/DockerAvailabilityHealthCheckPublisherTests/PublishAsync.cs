using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.DockerAvailability;
using Foundry.Modules.Workers.Features.Health;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.DockerAvailability.DockerAvailabilityHealthCheckPublisherTests;

/// <summary>
/// DockerAvailabilityChanged has only one consumer (DockerAvailabilityChangedBroadcastHandler:
/// in-memory state + SignalR). It is delivered directly via IIntegrationEventProcessor — no
/// outbox row, no relay latency.
/// </summary>
public sealed class PublishAsync
{
    private sealed class CapturingIntegrationEventProcessor : IIntegrationEventProcessor
    {
        private readonly List<IIntegrationEvent> _captured = [];

        public IReadOnlyList<IIntegrationEvent> Captured => _captured;

        public Task ProcessAsync(Guid eventId, IIntegrationEvent @event, CancellationToken cancellationToken)
        {
            _captured.Add(@event);
            return Task.CompletedTask;
        }
    }

    private static (DockerAvailabilityHealthCheckPublisher Publisher, CapturingIntegrationEventProcessor Processor) Build()
    {
        CapturingIntegrationEventProcessor processor = new();

        ServiceCollection services = new();
        services.AddScoped<IIntegrationEventProcessor>(_ => processor);
        ServiceProvider sp = services.BuildServiceProvider();

        DockerAvailabilityHealthCheckPublisher publisher = new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DockerAvailabilityHealthCheckPublisher>.Instance);

        return (publisher, processor);
    }

    private static HealthReport BuildReport(string checkName, HealthStatus status)
    {
        Dictionary<string, HealthReportEntry> entries = new()
        {
            [checkName] = new HealthReportEntry(
                status,
                description: null,
                duration: TimeSpan.Zero,
                exception: null,
                data: null),
        };

        return new HealthReport(entries, TimeSpan.Zero);
    }

    [Fact]
    public async Task WhenFirstPublishAndDockerDaemonHealthy_DeliversAvailableTrueEvent()
    {
        // Arrange
        (DockerAvailabilityHealthCheckPublisher publisher, CapturingIntegrationEventProcessor processor) = Build();
        HealthReport report = BuildReport(DockerDaemonHealthCheck.CheckName, HealthStatus.Healthy);

        // Act
        await publisher.PublishAsync(report, CancellationToken.None);

        // Assert
        DockerAvailabilityChanged delivered = processor.Captured.ShouldHaveSingleItem().ShouldBeOfType<DockerAvailabilityChanged>();
        delivered.IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenFirstPublishAndDockerDaemonUnhealthy_DeliversAvailableFalseEvent()
    {
        // Arrange
        (DockerAvailabilityHealthCheckPublisher publisher, CapturingIntegrationEventProcessor processor) = Build();
        HealthReport report = BuildReport(DockerDaemonHealthCheck.CheckName, HealthStatus.Unhealthy);

        // Act
        await publisher.PublishAsync(report, CancellationToken.None);

        // Assert
        DockerAvailabilityChanged delivered = processor.Captured.ShouldHaveSingleItem().ShouldBeOfType<DockerAvailabilityChanged>();
        delivered.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenSecondPublishWithSameStatus_DeliversNothing()
    {
        // Arrange
        (DockerAvailabilityHealthCheckPublisher publisher, CapturingIntegrationEventProcessor processor) = Build();
        HealthReport firstReport = BuildReport(DockerDaemonHealthCheck.CheckName, HealthStatus.Healthy);
        HealthReport secondReport = BuildReport(DockerDaemonHealthCheck.CheckName, HealthStatus.Healthy);

        // Act
        await publisher.PublishAsync(firstReport, CancellationToken.None);
        await publisher.PublishAsync(secondReport, CancellationToken.None);

        // Assert
        processor.Captured.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenStatusFlipsFromHealthyToUnhealthy_DeliversAvailableFalseEvent()
    {
        // Arrange
        (DockerAvailabilityHealthCheckPublisher publisher, CapturingIntegrationEventProcessor processor) = Build();
        HealthReport healthyReport = BuildReport(DockerDaemonHealthCheck.CheckName, HealthStatus.Healthy);
        HealthReport unhealthyReport = BuildReport(DockerDaemonHealthCheck.CheckName, HealthStatus.Unhealthy);

        // Act
        await publisher.PublishAsync(healthyReport, CancellationToken.None);
        await publisher.PublishAsync(unhealthyReport, CancellationToken.None);

        // Assert
        processor.Captured.Count.ShouldBe(2);
        DockerAvailabilityChanged secondEvent = processor.Captured[1].ShouldBeOfType<DockerAvailabilityChanged>();
        secondEvent.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenReportHasNoDockerDaemonEntry_DeliversNothing()
    {
        // Arrange
        (DockerAvailabilityHealthCheckPublisher publisher, CapturingIntegrationEventProcessor processor) = Build();
        HealthReport report = BuildReport("some-other-check", HealthStatus.Healthy);

        // Act
        await publisher.PublishAsync(report, CancellationToken.None);

        // Assert
        processor.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenFirstDeliveryThrows_SecondPublishWithSameStatusRetries()
    {
        // Arrange
        bool firstCall = true;
        CapturingIntegrationEventProcessor successProcessor = new();

        ServiceCollection services = new();
        services.AddScoped<IIntegrationEventProcessor>(_ =>
        {
            if (firstCall)
            {
                firstCall = false;
                return new ThrowingIntegrationEventProcessor();
            }

            return successProcessor;
        });
        ServiceProvider sp = services.BuildServiceProvider();

        DockerAvailabilityHealthCheckPublisher publisher = new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DockerAvailabilityHealthCheckPublisher>.Instance);

        HealthReport report = BuildReport(DockerDaemonHealthCheck.CheckName, HealthStatus.Healthy);

        // Act — first delivery throws, second should retry (same status) and deliver
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await publisher.PublishAsync(report, CancellationToken.None));
        await publisher.PublishAsync(report, CancellationToken.None);

        // Assert — the event was delivered on the second publish (transition retried)
        DockerAvailabilityChanged delivered = successProcessor.Captured.ShouldHaveSingleItem().ShouldBeOfType<DockerAvailabilityChanged>();
        delivered.IsAvailable.ShouldBeTrue();
    }

    private sealed class ThrowingIntegrationEventProcessor : IIntegrationEventProcessor
    {
        public Task ProcessAsync(Guid eventId, IIntegrationEvent @event, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Delivery failed.");
    }
}
