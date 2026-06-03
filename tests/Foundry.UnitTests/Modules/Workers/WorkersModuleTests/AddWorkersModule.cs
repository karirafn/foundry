using Docker.DotNet;

using Foundry.Modules.Workers;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public void WhenCalled_RegistersWorkerOptions()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Workers:MaxConcurrent"] = "5",
            ["Workers:ApiKey"] = "sk-ant-test",
        });
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IOptions<WorkerOptions> options = provider.GetRequiredService<IOptions<WorkerOptions>>();
        options.Value.MaxConcurrent.ShouldBe(5);
    }

    [Fact]
    public void WhenCalled_RegistersIWorkerOrchestrator()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Workers:ApiKey"] = "sk-ant-test",
        });
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
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Workers:ApiKey"] = "sk-ant-test",
        });
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert — accessing Value with empty ApiKey triggers validation failure
        IConfiguration emptyConfig = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection servicesWithEmptyKey = new();
        servicesWithEmptyKey.AddWorkersModule(emptyConfig);
        ServiceProvider providerWithEmptyKey = servicesWithEmptyKey.BuildServiceProvider();

        IOptions<WorkerOptions> options = providerWithEmptyKey.GetRequiredService<IOptions<WorkerOptions>>();
        Should.Throw<OptionsValidationException>(() => _ = options.Value);
    }

    [Fact]
    public void WhenCalled_RegistersWorkerDispatchServiceAsHostedService()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Workers:ApiKey"] = "sk-ant-test",
        });
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IHostedService> hostedServices = provider.GetServices<IHostedService>();
        hostedServices.ShouldContain(s => s is WorkerDispatchService);
    }

    [Fact]
    public void WhenCalled_RegistersWorkerImageBuildServiceAsHostedService()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Workers:ApiKey"] = "sk-ant-test",
        });
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IEnumerable<IHostedService> hostedServices = provider.GetServices<IHostedService>();
        hostedServices.ShouldContain(s => s.GetType().Name == "WorkerImageBuildService");
    }

    [Fact]
    public void WhenCalled_RegistersIImageOperations()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Workers:ApiKey"] = "sk-ant-test",
        });
        ServiceCollection services = new();

        // Act
        services.AddWorkersModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IImageOperations imageOperations = provider.GetRequiredService<IImageOperations>();
        imageOperations.ShouldNotBeNull();
    }
}
