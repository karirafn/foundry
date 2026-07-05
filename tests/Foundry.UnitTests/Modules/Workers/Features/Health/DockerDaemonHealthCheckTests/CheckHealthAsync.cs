using Docker.DotNet;

using Foundry.Modules.Workers.Features.Health;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Health.DockerDaemonHealthCheckTests;

public sealed class CheckHealthAsync
{
    private static HealthCheckContext CreateContext()
    {
        HealthCheckRegistration registration = new("docker-daemon", _ => null!, null, ["ready"]);
        return new HealthCheckContext { Registration = registration };
    }

    [Fact]
    public async Task WhenPingSucceeds_ReturnsHealthy()
    {
        // Arrange
        ISystemOperations systemOperations = new FakeSystemOperations().WithSuccessfulPing();
        DockerDaemonHealthCheck sut = new(systemOperations, TimeSpan.FromSeconds(3));
        HealthCheckContext context = CreateContext();

        // Act
        HealthCheckResult result = await sut.CheckHealthAsync(context, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task WhenPingThrowsHttpRequestException_ReturnsUnhealthy()
    {
        // Arrange
        ISystemOperations systemOperations = new FakeSystemOperations().WithFailedPing();
        DockerDaemonHealthCheck sut = new(systemOperations, TimeSpan.FromSeconds(3));
        HealthCheckContext context = CreateContext();

        // Act
        HealthCheckResult result = await sut.CheckHealthAsync(context, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.Status.ShouldBe(HealthStatus.Unhealthy),
            () => result.Description.ShouldNotBeNull().ShouldContain("Docker daemon"));
    }

    [Fact]
    public async Task WhenPingTimesOut_ReturnsUnhealthy()
    {
        // Arrange
        ISystemOperations systemOperations = new FakeSystemOperations().WithTimeoutPing();
        DockerDaemonHealthCheck sut = new(systemOperations, TimeSpan.FromMilliseconds(1));
        HealthCheckContext context = CreateContext();

        // Act
        HealthCheckResult result = await sut.CheckHealthAsync(context, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.Status.ShouldBe(HealthStatus.Unhealthy),
            () => result.Description.ShouldNotBeNull().ShouldContain("Docker daemon"));
    }
}
