using Foundry.Modules.Credentials.Contracts.Queries;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerRunStarterTests;

/// <summary>
/// Takes a claim the way production does: <see cref="IssueClaimedHandler"/> commits the starting run, then one
/// <see cref="WorkerRunStarter"/> tick starts it with the given collaborators. Every scope shares one
/// <see cref="DbContext"/>, so a test reads what the start committed through the context it seeded.
/// </summary>
internal sealed class ClaimedRunPipeline
{
    private readonly DbContext _dbContext;
    private readonly IGlobalSettingsQueries _settingsQueries;
    private readonly IWorkerRoleCatalogStore _roleCatalog;
    private readonly ServiceProvider _services;

    public ClaimedRunPipeline(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IPublisher publisher,
        IDomainEventDispatcher domainEventDispatcher,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IGlobalSettingsQueries settingsQueries,
        ICredentialQueries credentialQueries,
        IPostExitProviderQueries postExitProviderQueries,
        IFailingJobLogQueries failingJobLogQueries,
        IWorkerRoleCatalogStore roleCatalog,
        IRepositoryDispatchQueries repositoryDispatchQueries,
        IIssueContentQueries issueContentQueries,
        ITrackerContextQueries trackerContextQueries,
        ISystemNotificationBroadcaster broadcaster,
        IWorkerHostEntryResolver hostEntryResolver,
        ILogger<WorkerRunStart> logger)
    {
        _dbContext = dbContext;
        _settingsQueries = settingsQueries;
        _roleCatalog = roleCatalog;

        ServiceCollection services = new();
        services.AddSingleton(dbContext);
        services.AddSingleton(orchestrator);
        services.AddSingleton(publisher);
        services.AddSingleton(domainEventDispatcher);
        services.AddSingleton(integrationEventDispatcher);
        services.AddSingleton(settingsQueries);
        services.AddSingleton(credentialQueries);
        services.AddSingleton(postExitProviderQueries);
        services.AddSingleton(failingJobLogQueries);
        services.AddSingleton(roleCatalog);
        services.AddSingleton(repositoryDispatchQueries);
        services.AddSingleton(issueContentQueries);
        services.AddSingleton(trackerContextQueries);
        services.AddSingleton(broadcaster);
        services.AddSingleton(hostEntryResolver);
        services.AddSingleton(logger);
        services.AddScoped<WorkerRunStart>();
        _services = services.BuildServiceProvider();
    }

    public async Task HandleAsync(IssueClaimed @event, CancellationToken cancellationToken)
    {
        WorkerRunStartSignal signal = new();
        IssueClaimedHandler handler = new(
            _dbContext,
            _settingsQueries,
            _roleCatalog,
            signal,
            NullLogger<IssueClaimedHandler>.Instance);
        await handler.HandleAsync(@event, cancellationToken);

        WorkerRunStarter starter = new(
            _services.GetRequiredService<IServiceScopeFactory>(),
            signal,
            NullLogger<WorkerRunStarter>.Instance);
        await starter.TickForTest(cancellationToken);
    }
}
