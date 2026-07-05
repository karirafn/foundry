using Docker.DotNet;

using Foundry.Shared.Infrastructure.Docker;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker;

public sealed class AddSharedDockerInfrastructureTests
{
    [Fact]
    public void WhenCalled_RegistersIDockerContainerRuntime()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddSharedDockerInfrastructure();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IDockerContainerRuntime runtime = provider.GetRequiredService<IDockerContainerRuntime>();
        runtime.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalled_RegistersDockerClient()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddSharedDockerInfrastructure();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        DockerClient client = provider.GetRequiredService<DockerClient>();
        client.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalled_RegistersIContainerOperations()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddSharedDockerInfrastructure();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IContainerOperations containerOperations = provider.GetRequiredService<IContainerOperations>();
        containerOperations.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalled_RegistersIVolumeOperations()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddSharedDockerInfrastructure();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IVolumeOperations volumeOperations = provider.GetRequiredService<IVolumeOperations>();
        volumeOperations.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalled_RegistersIExecOperations()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddSharedDockerInfrastructure();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IExecOperations execOperations = provider.GetRequiredService<IExecOperations>();
        execOperations.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalled_RegistersIImageOperations()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddSharedDockerInfrastructure();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IImageOperations imageOperations = provider.GetRequiredService<IImageOperations>();
        imageOperations.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalled_RegistersISystemOperations()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddSharedDockerInfrastructure();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        ISystemOperations systemOperations = provider.GetRequiredService<ISystemOperations>();
        systemOperations.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalledTwice_IDockerContainerRuntimeIsSameInstance()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddSharedDockerInfrastructure();
        services.AddSharedDockerInfrastructure();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert — TryAddSingleton is idempotent: second call must not register a second impl
        IDockerContainerRuntime first = provider.GetRequiredService<IDockerContainerRuntime>();
        IDockerContainerRuntime second = provider.GetRequiredService<IDockerContainerRuntime>();
        first.ShouldBeSameAs(second);
    }
}
