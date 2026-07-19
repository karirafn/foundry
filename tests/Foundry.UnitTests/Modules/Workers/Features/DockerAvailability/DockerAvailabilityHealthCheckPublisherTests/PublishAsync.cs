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

public sealed class PublishAsync
{

    private sealed class CapturingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        private readonly List<IIntegrationEvent> _captured = [];

        public IReadOnlyList<IIntegrationEvent> Captured => _captured;

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            _captured.AddRange(events);
            return Task.CompletedTask;
        }
    }

    private static (DockerAvailabilityHealthCheckPublisher Publisher, CapturingIntegrationEventDispatcher Dispatcher) Build()
    {
        CapturingIntegrationEventDispatcher dispatcher = new();

        ServiceCollection services = new();
        services.AddScoped<IIntegrationEventDispatcher>(_ => dispatcher);
        ServiceProvider sp = services.BuildServiceProvider();

        DockerAvailabilityHealthCheckPublisher publisher = new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DockerAvailabilityHealthCheckPublisher>.Instance);

        return (publisher, dispatcher);
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
    public async Task WhenFirstPublishAndDockerDaemonHealthy_DispatchesAvailableTrueEvent()
    {
        // Arrange
        (DockerAvailabilityHealthCheckPublisher publisher, CapturingIntegrationEventDispatcher dispatcher) = Build();
        HealthReport report = BuildReport(DockerDaemonHealthCheck.CheckName, HealthStatus.Healthy);

        // Act
        await publisher.PublishAsync(report, CancellationToken.None);

        // Assert
        DockerAvailabilityChanged dispatched = dispatcher.Captured.ShouldHaveSingleItem().ShouldBeOfType<DockerAvailabilityChanged>();
        dispatched.IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenFirstPublishAndDockerDaemonUnhealthy_DispatchesAvailableFalseEvent()
    {
        // Arrange
        (DockerAvailabilityHealthCheckPublisher publisher, CapturingIntegrationEventDispatcher dispatcher) = Build();
        HealthReport report = BuildReport(DockerDaemonHealthCheck.CheckName, HealthStatus.Unhealthy);

        // Act
        await publisher.PublishAsync(report, CancellationToken.None);

        // Assert
        DockerAvailabilityChanged dispatched = dispatcher.Captured.ShouldHaveSingleItem().ShouldBeOfType<DockerAvailabilityChanged>();
        dispatched.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenSecondPublishWithSameStatus_DispatchesNothing()
    {
        // Arrange
        (DockerAvailabilityHealthCheckPublisher publisher, CapturingIntegrationEventDispatcher dispatcher) = Build();
        HealthReport firstReport = BuildReport(DockerDaemonHealthCheck.CheckName, HealthStatus.Healthy);
        HealthReport secondReport = BuildReport(DockerDaemonHealthCheck.CheckName, HealthStatus.Healthy);

        // Act
        await publisher.PublishAsync(firstReport, CancellationToken.None);
        await publisher.PublishAsync(secondReport, CancellationToken.None);

        // Assert
        dispatcher.Captured.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenStatusFlipsFromHealthyToUnhealthy_DispatchesAvailableFalseEvent()
    {
        // Arrange
        (DockerAvailabilityHealthCheckPublisher publisher, CapturingIntegrationEventDispatcher dispatcher) = Build();
        HealthReport healthyReport = BuildReport(DockerDaemonHealthCheck.CheckName, HealthStatus.Healthy);
        HealthReport unhealthyReport = BuildReport(DockerDaemonHealthCheck.CheckName, HealthStatus.Unhealthy);

        // Act
        await publisher.PublishAsync(healthyReport, CancellationToken.None);
        await publisher.PublishAsync(unhealthyReport, CancellationToken.None);

        // Assert
        dispatcher.Captured.Count.ShouldBe(2);
        DockerAvailabilityChanged secondEvent = dispatcher.Captured[1].ShouldBeOfType<DockerAvailabilityChanged>();
        secondEvent.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenReportHasNoDockerDaemonEntry_DispatchesNothing()
    {
        // Arrange
        (DockerAvailabilityHealthCheckPublisher publisher, CapturingIntegrationEventDispatcher dispatcher) = Build();
        HealthReport report = BuildReport("some-other-check", HealthStatus.Healthy);

        // Act
        await publisher.PublishAsync(report, CancellationToken.None);

        // Assert
        dispatcher.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenFirstDispatchThrows_SecondPublishWithSameStatusRetries()
    {
        // Arrange
        bool firstCall = true;
        CapturingIntegrationEventDispatcher successDispatcher = new();

        ServiceCollection services = new();
        services.AddScoped<IIntegrationEventDispatcher>(_ =>
        {
            if (firstCall)
            {
                firstCall = false;
                return new ThrowingIntegrationEventDispatcher();
            }

            return successDispatcher;
        });
        ServiceProvider sp = services.BuildServiceProvider();

        DockerAvailabilityHealthCheckPublisher publisher = new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DockerAvailabilityHealthCheckPublisher>.Instance);

        HealthReport report = BuildReport(DockerDaemonHealthCheck.CheckName, HealthStatus.Healthy);

        // Act — first publish throws, second should retry (same status) and dispatch
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await publisher.PublishAsync(report, CancellationToken.None));
        await publisher.PublishAsync(report, CancellationToken.None);

        // Assert — the event was dispatched on the second publish (transition retried)
        DockerAvailabilityChanged dispatched = successDispatcher.Captured.ShouldHaveSingleItem().ShouldBeOfType<DockerAvailabilityChanged>();
        dispatched.IsAvailable.ShouldBeTrue();
    }

    private sealed class ThrowingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Dispatch failed.");
    }
}
