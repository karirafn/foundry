using Docker.DotNet;

using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Features.Activity;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.DockerAvailability;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using DomainWorkerRunFailed = Foundry.Modules.Workers.Domain.Events.WorkerRunFailed;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.WorkersModuleTests;

public sealed class AddWorkersModule
{
    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "TestApp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Test";
    }

    private sealed class NullSystemNotificationBroadcaster : ISystemNotificationBroadcaster
    {
        public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class NullAgentActivityBroadcaster : IAgentActivityBroadcaster
    {
        public Task BroadcastActivityAsync(AgentActivity activity, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task BroadcastEndedAsync(AgentActivityEnded ended, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class NullLoginSessionState : ILoginSessionState
    {
        public bool IsLoginActive => false;
    }

    [Fact]
    public void WhenCalled_RegistersIWorkerOrchestrator()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IWorkerOrchestrator orchestrator = provider.GetRequiredService<IWorkerOrchestrator>();
        orchestrator.ShouldBeOfType<DockerWorkerOrchestrator>();
    }

    [Fact]
    public void WhenCalled_RegistersWorkerDispatchServiceAsHostedService()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());
        services.AddSingleton<ISystemNotificationBroadcaster>(new NullSystemNotificationBroadcaster());
        services.AddSingleton<ILoginSessionBroadcaster>(NullLoginSessionBroadcaster.Instance);
        services.AddSingleton<ILoginSessionState, NullLoginSessionState>();
        services.AddSingleton<IAgentActivityBroadcaster, NullAgentActivityBroadcaster>();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IHostedService> hostedServices = provider.GetServices<IHostedService>();
        hostedServices.ShouldContain(s => s is WorkerDispatchService);
    }

    [Fact]
    public void WhenCalled_RegistersAgentActivityFollowerServiceAsHostedService()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());
        services.AddSingleton<ISystemNotificationBroadcaster>(new NullSystemNotificationBroadcaster());
        services.AddSingleton<ILoginSessionBroadcaster>(NullLoginSessionBroadcaster.Instance);
        services.AddSingleton<ILoginSessionState, NullLoginSessionState>();
        services.AddSingleton<IAgentActivityBroadcaster, NullAgentActivityBroadcaster>();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IHostedService> hostedServices = provider.GetServices<IHostedService>();
        hostedServices.ShouldContain(s => s is AgentActivityFollowerService);
        provider.GetRequiredService<IAgentActivityQueries>().ShouldBeSameAs(provider.GetRequiredService<AgentActivityStore>());
    }

    [Fact]
    public void WhenCalled_RegistersWorkerImageRebuildServiceAsHostedService()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());
        services.AddSingleton<ISystemNotificationBroadcaster>(new NullSystemNotificationBroadcaster());
        services.AddSingleton<ILoginSessionBroadcaster>(NullLoginSessionBroadcaster.Instance);
        services.AddSingleton<ILoginSessionState, NullLoginSessionState>();
        services.AddSingleton<IAgentActivityBroadcaster, NullAgentActivityBroadcaster>();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IHostedService> hostedServices = provider.GetServices<IHostedService>();
        hostedServices.ShouldContain(s => s is WorkerImageRebuildService);
    }

    [Fact]
    public void WhenCalled_RegistersStaleStartingRunServiceAsHostedService()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());
        services.AddSingleton<ISystemNotificationBroadcaster>(new NullSystemNotificationBroadcaster());
        services.AddSingleton<ILoginSessionBroadcaster>(NullLoginSessionBroadcaster.Instance);
        services.AddSingleton<ILoginSessionState, NullLoginSessionState>();
        services.AddSingleton<IAgentActivityBroadcaster, NullAgentActivityBroadcaster>();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IHostedService> hostedServices = provider.GetServices<IHostedService>();
        hostedServices.ShouldContain(s => s is StaleStartingRunService);
    }

    [Fact]
    public void WhenCalled_RegistersIImageOperations()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IImageOperations imageOperations = provider.GetRequiredService<IImageOperations>();
        imageOperations.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalled_RegistersIContainerOperations()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IContainerOperations containerOperations = provider.GetRequiredService<IContainerOperations>();
        containerOperations.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalled_RegistersIExecOperations()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IExecOperations execOperations = provider.GetRequiredService<IExecOperations>();
        execOperations.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalled_RegistersIContainerOutputParserAsSingleton()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddLogging();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IContainerOutputParser parser = provider.GetRequiredService<IContainerOutputParser>();
        parser.ShouldBeOfType<ContainerOutputParser>();
    }

    [Fact]
    public void WhenCalled_RegistersDispatchPausedBroadcastHandler()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<ISystemNotificationBroadcaster>(new NullSystemNotificationBroadcaster());

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IIntegrationEventHandler<DispatchPaused>> handlers =
            provider.GetServices<IIntegrationEventHandler<DispatchPaused>>();
        handlers.ShouldContain(h => h is DispatchPausedBroadcastHandler);
    }

    [Fact]
    public void WhenCalled_RegistersDispatchResumedBroadcastHandler()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<ISystemNotificationBroadcaster>(new NullSystemNotificationBroadcaster());

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IIntegrationEventHandler<DispatchResumed>> handlers =
            provider.GetServices<IIntegrationEventHandler<DispatchResumed>>();
        handlers.ShouldContain(h => h is DispatchResumedBroadcastHandler);
    }

    [Fact]
    public void WhenCalled_RegistersImageBuildStartedBroadcastHandler()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<ISystemNotificationBroadcaster>(new NullSystemNotificationBroadcaster());

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IIntegrationEventHandler<ImageBuildStarted>> handlers =
            provider.GetServices<IIntegrationEventHandler<ImageBuildStarted>>();
        handlers.ShouldContain(h => h is ImageBuildStartedBroadcastHandler);
    }

    [Fact]
    public void WhenCalled_RegistersImageBuildCompletedBroadcastHandler()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<ISystemNotificationBroadcaster>(new NullSystemNotificationBroadcaster());

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IIntegrationEventHandler<ImageBuildCompleted>> handlers =
            provider.GetServices<IIntegrationEventHandler<ImageBuildCompleted>>();
        handlers.ShouldContain(h => h is ImageBuildCompletedBroadcastHandler);
    }

    [Fact]
    public void WhenCalled_RegistersImageBuildFailedBroadcastHandler()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<ISystemNotificationBroadcaster>(new NullSystemNotificationBroadcaster());

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IIntegrationEventHandler<ImageBuildFailed>> handlers =
            provider.GetServices<IIntegrationEventHandler<ImageBuildFailed>>();
        handlers.ShouldContain(h => h is ImageBuildFailedBroadcastHandler);
    }

    [Fact]
    public void WhenCalled_RegistersISystemOperations()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        ISystemOperations systemOperations = provider.GetRequiredService<ISystemOperations>();
        systemOperations.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalled_RegistersDockerDaemonHealthCheckWithReadyTag()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        HealthCheckServiceOptions options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        options.Registrations.ShouldContain(r => r.Name == "docker-daemon");
        HealthCheckRegistration registration = options.Registrations.Single(r => r.Name == "docker-daemon");
        registration.Tags.ShouldContain("ready");
    }

    [Fact]
    public void WhenCalled_RegistersIDockerAvailabilityState()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IDockerAvailabilityState state = provider.GetRequiredService<IDockerAvailabilityState>();
        state.ShouldBeOfType<DockerAvailabilityState>();
    }

    [Fact]
    public void WhenCalled_RegistersIDockerAvailabilityStateMutator()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IDockerAvailabilityStateMutator mutator = provider.GetRequiredService<IDockerAvailabilityStateMutator>();
        mutator.ShouldBeOfType<DockerAvailabilityState>();
    }

    [Fact]
    public void WhenCalled_RegistersDockerAvailabilityHealthCheckPublisher()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddLogging();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IHealthCheckPublisher> publishers = provider.GetServices<IHealthCheckPublisher>();
        publishers.ShouldContain(p => p is DockerAvailabilityHealthCheckPublisher);
    }

    [Fact]
    public void WhenCalled_RegistersDockerAvailabilityChangedBroadcastHandler()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddSingleton<ISystemNotificationBroadcaster>(new NullSystemNotificationBroadcaster());

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IIntegrationEventHandler<DockerAvailabilityChanged>> handlers =
            provider.GetServices<IIntegrationEventHandler<DockerAvailabilityChanged>>();
        handlers.ShouldContain(h => h is DockerAvailabilityChangedBroadcastHandler);
    }

    [Fact]
    public void WhenCalled_SetsHealthCheckPublisherPeriodTo15Seconds()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IOptions<HealthCheckPublisherOptions> options = provider.GetRequiredService<IOptions<HealthCheckPublisherOptions>>();
        options.Value.Period.ShouldBe(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void WhenCalled_RegistersExactlyOneWorkerRunFailedBridgeHandler()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddScoped<IIntegrationEventDispatcher, NullIntegrationEventDispatcher>();

        // Act
        services.AddWorkersModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IDomainEventHandler<DomainWorkerRunFailed>> handlers =
            provider.GetServices<IDomainEventHandler<DomainWorkerRunFailed>>();
        handlers.ShouldHaveSingleItem().ShouldBeOfType<WorkerRunFailedBridgeHandler>();
    }

    private sealed class NullIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
