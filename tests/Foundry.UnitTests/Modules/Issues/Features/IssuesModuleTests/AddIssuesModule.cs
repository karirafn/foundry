using Foundry.Modules.Issues;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

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
        services.AddScoped<IRepositorySlugQueries, NullRepositorySlugQueries>();
        services.AddScoped<IRepositoryEligibilityQuery, NullRepositoryEligibilityQuery>();
        services.AddScoped<IIssueBroadcaster, NullIssueBroadcaster>();
        services.AddScoped<IAuthValidator, NullAuthValidator>();
        services.AddScoped<ISystemNotificationBroadcaster, NullSystemNotificationBroadcaster>();
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

    [Fact]
    public void WhenServicesRegistered_WorkerRunCompletedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IIntegrationEventHandler<WorkerRunCompleted> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<WorkerRunCompleted>>();
        handler.ShouldBeOfType<WorkerRunCompletedHandler>();
    }

    [Fact]
    public void WhenServicesRegistered_WorkerRunFailedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IIntegrationEventHandler<WorkerRunFailed> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<WorkerRunFailed>>();
        handler.ShouldBeOfType<WorkerRunFailedHandler>();
    }

    [Fact]
    public void WhenServicesRegistered_ProviderIssueClosedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IIntegrationEventHandler<ProviderIssueClosed> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<ProviderIssueClosed>>();
        handler.ShouldBeOfType<ProviderIssueClosedHandler>();
    }

    [Fact]
    public void WhenServicesRegistered_ProviderPullRequestClosedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IIntegrationEventHandler<ProviderPullRequestClosed> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<ProviderPullRequestClosed>>();
        handler.ShouldBeOfType<ProviderPullRequestClosedHandler>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueQueuedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IDomainEventHandler<IssueQueued> handler =
            scope.ServiceProvider.GetRequiredService<IDomainEventHandler<IssueQueued>>();
        handler.ShouldBeOfType<IssueStateChangedAdapter<IssueQueued>>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueBlockedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IDomainEventHandler<IssueBlocked> handler =
            scope.ServiceProvider.GetRequiredService<IDomainEventHandler<IssueBlocked>>();
        handler.ShouldBeOfType<IssueStateChangedAdapter<IssueBlocked>>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueCompletedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IDomainEventHandler<IssueCompleted> handler =
            scope.ServiceProvider.GetRequiredService<IDomainEventHandler<IssueCompleted>>();
        handler.ShouldBeOfType<IssueStateChangedAdapter<IssueCompleted>>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueFailedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IDomainEventHandler<IssueFailed> handler =
            scope.ServiceProvider.GetRequiredService<IDomainEventHandler<IssueFailed>>();
        handler.ShouldBeOfType<IssueStateChangedAdapter<IssueFailed>>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueInReviewHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IDomainEventHandler<IssueInReview> handler =
            scope.ServiceProvider.GetRequiredService<IDomainEventHandler<IssueInReview>>();
        handler.ShouldBeOfType<IssueStateChangedAdapter<IssueInReview>>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueUnchangedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IDomainEventHandler<IssueUnchanged> handler =
            scope.ServiceProvider.GetRequiredService<IDomainEventHandler<IssueUnchanged>>();
        handler.ShouldBeOfType<IssueStateChangedAdapter<IssueUnchanged>>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueDismissedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IDomainEventHandler<IssueDismissed> handler =
            scope.ServiceProvider.GetRequiredService<IDomainEventHandler<IssueDismissed>>();
        handler.ShouldBeOfType<IssueStateChangedAdapter<IssueDismissed>>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueRevisionQueuedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IDomainEventHandler<IssueRevisionQueued> handler =
            scope.ServiceProvider.GetRequiredService<IDomainEventHandler<IssueRevisionQueued>>();
        handler.ShouldBeOfType<IssueStateChangedAdapter<IssueRevisionQueued>>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueRevisionFailedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IDomainEventHandler<IssueRevisionFailed> handler =
            scope.ServiceProvider.GetRequiredService<IDomainEventHandler<IssueRevisionFailed>>();
        handler.ShouldBeOfType<IssueStateChangedAdapter<IssueRevisionFailed>>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueInProgressHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IDomainEventHandler<IssueInProgress> handler =
            scope.ServiceProvider.GetRequiredService<IDomainEventHandler<IssueInProgress>>();
        handler.ShouldBeOfType<IssueStateChangedAdapter<IssueInProgress>>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueRevisionInProgressHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IDomainEventHandler<IssueRevisionInProgress> handler =
            scope.ServiceProvider.GetRequiredService<IDomainEventHandler<IssueRevisionInProgress>>();
        handler.ShouldBeOfType<IssueStateChangedAdapter<IssueRevisionInProgress>>();
    }

    [Fact]
    public void WhenServicesRegistered_DispatchResumedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IIntegrationEventHandler<DispatchResumed> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<DispatchResumed>>();
        handler.ShouldBeOfType<DispatchResumedHandler>();
    }

    private sealed class NullIssueBroadcaster : IIssueBroadcaster
    {
        public Task BroadcastAsync(IssueSummary summary, CancellationToken cancellationToken)
            => Task.CompletedTask;
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

    private sealed class NullRepositoryEligibilityQuery : IRepositoryEligibilityQuery
    {
        public Task<RepositoryEligibilityInfo?> GetEligibilityAsync(
            Guid repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<RepositoryEligibilityInfo?>(null);

        public Task<IReadOnlySet<Guid>> GetEligibleRepositoryIdsAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

        public Task<IReadOnlyDictionary<Guid, string>> GetEligibilityStatusesAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class NullAuthValidator : IAuthValidator
    {
        public Task<AuthValidationResult> ValidateAsync(CancellationToken cancellationToken)
            => Task.FromResult(AuthValidationResult.Valid());
    }

    private sealed class NullSystemNotificationBroadcaster : ISystemNotificationBroadcaster
    {
        public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
