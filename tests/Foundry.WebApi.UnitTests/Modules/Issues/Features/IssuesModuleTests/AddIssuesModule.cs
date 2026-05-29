using Foundry.Modules.Issues;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class AddIssuesModule : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public AddIssuesModule()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        ServiceCollection services = new();

        services.AddDbContext<FoundryDbContext>(opts =>
            opts.UseSqlite(_connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        services.AddLogging();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IIntegrationEventDispatcher, NullIntegrationEventDispatcher>();
        services.AddScoped<IRepositoryDispatchQueries, NullRepositoryDispatchQueries>();
        services.AddIssuesModule();

        _serviceProvider = services.BuildServiceProvider();

        using IServiceScope scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<FoundryDbContext>().Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public void WhenServicesRegistered_IIssueQueriesResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IIssueQueries queries = scope.ServiceProvider.GetRequiredService<IIssueQueries>();
        queries.ShouldBeOfType<IssueQueries>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueDetectedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IIntegrationEventHandler<IssueDetected> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<IssueDetected>>();
        handler.ShouldBeOfType<CreateIssueHandler>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueDetailsChangedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IIntegrationEventHandler<IssueDetailsChanged> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<IssueDetailsChanged>>();
        handler.ShouldBeOfType<UpdateIssueDetailsHandler>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueDependenciesDetectedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IIntegrationEventHandler<IssueDependenciesDetected> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<IssueDependenciesDetected>>();
        handler.ShouldBeOfType<ProcessIssueDependenciesHandler>();
    }

    [Fact]
    public void WhenServicesRegistered_WorkerCapacityAvailableHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IIntegrationEventHandler<WorkerCapacityAvailable> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<WorkerCapacityAvailable>>();
        handler.ShouldBeOfType<WorkerCapacityAvailableHandler>();
    }

    private sealed class NullIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class NullRepositoryDispatchQueries : IRepositoryDispatchQueries
    {
        public Task<RepositoryDispatchInfo?> GetDispatchInfoAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<RepositoryDispatchInfo?>(null);
    }
}
