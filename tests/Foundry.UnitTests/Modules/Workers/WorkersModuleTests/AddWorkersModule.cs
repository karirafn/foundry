using Docker.DotNet;

using Foundry.Modules.Workers;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Modules.Workers.Features.Login;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Configuration;
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
    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

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

    [Fact]
    public void WhenCalled_RegistersWorkerOptions()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Workers:Image"] = "ghcr.io/anthropics/claude-code:v1.0",
        });
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IOptions<WorkerOptions> options = provider.GetRequiredService<IOptions<WorkerOptions>>();
        options.Value.Image.ShouldBe("ghcr.io/anthropics/claude-code:v1.0");
    }

    [Fact]
    public void WhenCalled_RegistersIWorkerOrchestrator()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IWorkerOrchestrator orchestrator = provider.GetRequiredService<IWorkerOrchestrator>();
        orchestrator.ShouldBeOfType<DockerWorkerOrchestrator>();
    }

    [Fact]
    public void WhenCalled_RegistersWorkerOptionsValidator()
    {
        // Arrange — empty image triggers validation failure
        IConfiguration emptyConfig = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Workers:Image"] = string.Empty,
        });
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule(emptyConfig);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert — accessing Value with empty Image triggers validation failure
        IOptions<WorkerOptions> options = provider.GetRequiredService<IOptions<WorkerOptions>>();
        Should.Throw<OptionsValidationException>(() => _ = options.Value);
    }

    [Fact]
    public void WhenCalled_RegistersWorkerDispatchServiceAsHostedService()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());
        services.AddSingleton<ISystemNotificationBroadcaster>(new NullSystemNotificationBroadcaster());
        services.AddSingleton<ILoginSessionBroadcaster>(NullLoginSessionBroadcaster.Instance);

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IHostedService> hostedServices = provider.GetServices<IHostedService>();
        hostedServices.ShouldContain(s => s is WorkerDispatchService);
    }

    [Fact]
    public void WhenCalled_RegistersWorkerImageRebuildServiceAsHostedService()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());
        services.AddSingleton<ISystemNotificationBroadcaster>(new NullSystemNotificationBroadcaster());
        services.AddSingleton<ILoginSessionBroadcaster>(NullLoginSessionBroadcaster.Instance);

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IHostedService> hostedServices = provider.GetServices<IHostedService>();
        hostedServices.ShouldContain(s => s is WorkerImageRebuildService);
    }

    [Fact]
    public void WhenCalled_RegistersIImageOperations()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IImageOperations imageOperations = provider.GetRequiredService<IImageOperations>();
        imageOperations.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalled_RegistersIContainerOperations()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IContainerOperations containerOperations = provider.GetRequiredService<IContainerOperations>();
        containerOperations.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalled_RegistersIExecOperations()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IExecOperations execOperations = provider.GetRequiredService<IExecOperations>();
        execOperations.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalled_RegistersIContainerOutputParserAsSingleton()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IContainerOutputParser parser = provider.GetRequiredService<IContainerOutputParser>();
        parser.ShouldBeOfType<ContainerOutputParser>();
    }

    [Fact]
    public void WhenCalled_RegistersDispatchPausedBroadcastHandler()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();
        services.AddSingleton<ISystemNotificationBroadcaster>(new NullSystemNotificationBroadcaster());

        // Act
        services.AddWorkersModule(configuration);
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
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();
        services.AddSingleton<ISystemNotificationBroadcaster>(new NullSystemNotificationBroadcaster());

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IIntegrationEventHandler<DispatchResumed>> handlers =
            provider.GetServices<IIntegrationEventHandler<DispatchResumed>>();
        handlers.ShouldContain(h => h is DispatchResumedBroadcastHandler);
    }

    [Fact]
    public void WhenCalled_RegistersLoginSessionServiceAsSingleton()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ILoginSessionBroadcaster>(NullLoginSessionBroadcaster.Instance);

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        LoginSessionService service = provider.GetRequiredService<LoginSessionService>();
        service.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalled_RegistersILoginSessionStatePointingToLoginSessionService()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ILoginSessionBroadcaster>(NullLoginSessionBroadcaster.Instance);

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        ILoginSessionState state = provider.GetRequiredService<ILoginSessionState>();
        state.ShouldBeOfType<LoginSessionService>();
    }

    [Fact]
    public void WhenCalled_RegistersLoginContainerReaperAsHostedService()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());
        services.AddSingleton<ISystemNotificationBroadcaster>(new NullSystemNotificationBroadcaster());
        services.AddSingleton<ILoginSessionBroadcaster>(NullLoginSessionBroadcaster.Instance);

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IHostedService> hostedServices = provider.GetServices<IHostedService>();
        hostedServices.ShouldContain(s => s is LoginContainerReaper);
    }

    [Fact]
    public void WhenCalled_RegistersISystemOperations()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        ISystemOperations systemOperations = provider.GetRequiredService<ISystemOperations>();
        systemOperations.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCalled_RegistersDockerDaemonHealthCheckWithReadyTag()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        HealthCheckServiceOptions options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        options.Registrations.ShouldContain(r => r.Name == "docker-daemon");
        HealthCheckRegistration registration = options.Registrations.Single(r => r.Name == "docker-daemon");
        registration.Tags.ShouldContain("ready");
    }
}
