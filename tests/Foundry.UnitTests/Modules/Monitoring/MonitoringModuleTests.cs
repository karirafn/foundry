using Foundry.Modules.Monitoring;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring;

public sealed class MonitoringModuleTests
{
    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    [Fact]
    public void AddMonitoringModule_RegistersMonitoringOptions()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Monitoring:DefaultPollIntervalSeconds"] = "60",
        });
        ServiceCollection services = new();

        // Act
        services.AddMonitoringModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        IOptions<MonitoringOptions> options = provider.GetRequiredService<IOptions<MonitoringOptions>>();
        options.Value.DefaultPollIntervalSeconds.ShouldBe(60);
    }

    [Fact]
    public void AddMonitoringModule_RegistersIIssueProviderFactory()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();
        services.AddHttpClient();

        // Act
        services.AddMonitoringModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert — can resolve the factory
        using IServiceScope scope = provider.CreateScope();
        IIssueProviderFactory factory = scope.ServiceProvider.GetRequiredService<IIssueProviderFactory>();
        factory.ShouldNotBeNull();
    }

    [Fact]
    public void AddMonitoringModule_RegistersIRepositorySlugQueries()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();
        services.AddHttpClient();
        services.AddDbContext<DbContext, FoundryDbContext>(opts =>
            opts.UseSqlite(new SqliteConnectionStringBuilder { DataSource = ":memory:" }.ToString()));

        // Act
        services.AddMonitoringModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        using IServiceScope scope = provider.CreateScope();
        IRepositorySlugQueries queries = scope.ServiceProvider.GetRequiredService<IRepositorySlugQueries>();
        queries.ShouldNotBeNull();
    }

    [Fact]
    public void AddMonitoringModule_DoesNotRegisterIProviderAuth()
    {
        // Arrange — IProviderAuth is registered in Program.cs (composition root), not by the module.
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();
        services.AddHttpClient();
        services.AddSingleton(configuration);

        // Act
        services.AddMonitoringModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert — IProviderAuth is not resolvable from the module alone
        using IServiceScope scope = provider.CreateScope();
        IProviderAuth? auth = scope.ServiceProvider.GetService<IProviderAuth>();
        auth.ShouldBeNull();
    }

    [Fact]
    public void AddMonitoringModule_RegistersMonitoringServiceAsHostedService()
    {
        // Arrange
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>());
        ServiceCollection services = new();
        services.AddHttpClient();
        services.AddLogging();

        // Act
        services.AddMonitoringModule(configuration);
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert — MonitoringService is registered as a hosted service
        IEnumerable<IHostedService> hostedServices = provider.GetServices<IHostedService>();
        hostedServices.ShouldContain(s => s is MonitoringService);
    }

    [Fact]
    public void MapMonitoringEndpoints_ReturnsBuilder()
    {
        // Arrange
        Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app =
            new FakeEndpointRouteBuilder();

        // Act
        Microsoft.AspNetCore.Routing.IEndpointRouteBuilder result = app.MapMonitoringEndpoints();

        // Assert
        result.ShouldBeSameAs(app);
    }

    private sealed class FakeEndpointRouteBuilder : Microsoft.AspNetCore.Routing.IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider =>
            new ServiceCollection().BuildServiceProvider();

        public ICollection<Microsoft.AspNetCore.Routing.EndpointDataSource> DataSources { get; } = [];

        public Microsoft.AspNetCore.Builder.IApplicationBuilder CreateApplicationBuilder() =>
            new Microsoft.AspNetCore.Builder.ApplicationBuilder(ServiceProvider);
    }
}
