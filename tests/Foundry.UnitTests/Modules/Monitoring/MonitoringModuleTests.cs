using Foundry.Modules.Monitoring;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring;

public sealed class MonitoringModuleTests
{
    [Fact]
    public void AddMonitoringModule_RegistersIIssueProviderFactory()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddHttpClient();

        // Act
        services.AddMonitoringModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert — can resolve the factory
        using IServiceScope scope = provider.CreateScope();
        IIssueProviderFactory factory = scope.ServiceProvider.GetRequiredService<IIssueProviderFactory>();
        factory.ShouldNotBeNull();
    }

    [Fact]
    public void AddMonitoringModule_RegistersICredentialResolver()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddHttpClient();
        services.AddDbContext<DbContext, FoundryDbContext>(opts =>
            opts.UseSqlite(new SqliteConnectionStringBuilder { DataSource = ":memory:" }.ToString()));

        // Act
        services.AddMonitoringModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        using IServiceScope scope = provider.CreateScope();
        ICredentialResolver resolver = scope.ServiceProvider.GetRequiredService<ICredentialResolver>();
        resolver.ShouldNotBeNull();
    }

    [Fact]
    public void AddMonitoringModule_RegistersIRepositorySlugQueries()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddHttpClient();
        services.AddDbContext<DbContext, FoundryDbContext>(opts =>
            opts.UseSqlite(new SqliteConnectionStringBuilder { DataSource = ":memory:" }.ToString()));

        // Act
        services.AddMonitoringModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        using IServiceScope scope = provider.CreateScope();
        IRepositorySlugQueries queries = scope.ServiceProvider.GetRequiredService<IRepositorySlugQueries>();
        queries.ShouldNotBeNull();
    }

    [Fact]
    public void AddMonitoringModule_RegistersIPostExitProviderQueries()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddHttpClient();
        services.AddDbContext<DbContext, FoundryDbContext>(opts =>
            opts.UseSqlite(new SqliteConnectionStringBuilder { DataSource = ":memory:" }.ToString()));

        // Act
        services.AddMonitoringModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        using IServiceScope scope = provider.CreateScope();
        IPostExitProviderQueries queries =
            scope.ServiceProvider.GetRequiredService<IPostExitProviderQueries>();
        queries.ShouldNotBeNull();
    }

    [Fact]
    public void AddMonitoringModule_RegistersMonitoringServiceAsHostedService()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddHttpClient();
        services.AddLogging();

        // Act
        services.AddMonitoringModule();
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

    [Fact]
    public void AddMonitoringModule_GitHubHttpClient_HasMaxResponseContentBufferSize()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddHttpClient();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddMonitoringModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Act
        HttpClient client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(typeof(GitHubHttpClient).Name);

        // Assert
        client.MaxResponseContentBufferSize.ShouldBe(MonitoringModule.MaxResponseContentBufferSize);
    }

    [Fact]
    public void AddMonitoringModule_GitLabHttpClient_HasMaxResponseContentBufferSize()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddHttpClient();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddMonitoringModule();
        ServiceProvider provider = services.BuildServiceProvider();

        // Act
        HttpClient client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(typeof(GitLabHttpClient).Name);

        // Assert
        client.MaxResponseContentBufferSize.ShouldBe(MonitoringModule.MaxResponseContentBufferSize);
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
