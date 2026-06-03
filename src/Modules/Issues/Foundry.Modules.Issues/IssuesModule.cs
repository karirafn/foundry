using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Modules.Issues;

public static class IssuesModule
{
    public static IServiceCollection AddIssuesModule(this IServiceCollection services)
    {
        services.AddScoped<IIssueQueries, IssueQueries>();

        services.AddIntegrationEventHandler<IssueDetected, CreateIssueHandler>();
        services.AddIntegrationEventHandler<IssueDetailsChanged, UpdateIssueDetailsHandler>();
        services.AddIntegrationEventHandler<IssueDependenciesDetected, ProcessIssueDependenciesHandler>();
        services.AddIntegrationEventHandler<WorkerCapacityAvailable, WorkerCapacityAvailableHandler>();
        services.AddIntegrationEventHandler<WorkerRunCompleted, WorkerRunCompletedHandler>();
        services.AddIntegrationEventHandler<WorkerRunFailed, WorkerRunFailedHandler>();
        services.AddIntegrationEventHandler<ProviderIssueClosed, ProviderIssueClosedHandler>();
        services.AddIntegrationEventHandler<ProviderPullRequestClosed, ProviderPullRequestClosedHandler>();
        services.AddIntegrationEventHandler<PullRequestChangesRequested, PullRequestChangesRequestedHandler>();

        services.AddScoped<IssueStateChangedHandler>();
        AddIssueStateChangedHandler<IssueQueued>(services);
        AddIssueStateChangedHandler<IssueBlocked>(services);
        AddIssueStateChangedHandler<IssueIneligible>(services);
        AddIssueStateChangedHandler<IssueCompleted>(services);
        AddIssueStateChangedHandler<IssueFailed>(services);
        AddIssueStateChangedHandler<IssueInReview>(services);
        AddIssueStateChangedHandler<IssueUnchanged>(services);
        AddIssueStateChangedHandler<IssueDismissed>(services);
        AddIssueStateChangedHandler<IssueRevisionQueued>(services);
        AddIssueStateChangedHandler<IssueRevisionFailed>(services);

        return services;
    }

    private static void AddIssueStateChangedHandler<TEvent>(IServiceCollection services)
        where TEvent : IDomainEvent, IIssueStateChanged
    {
        services.AddScoped<IDomainEventHandler<TEvent>>(sp =>
            new IssueStateChangedAdapter<TEvent>(sp.GetRequiredService<IssueStateChangedHandler>()));
    }

    public static IEndpointRouteBuilder MapIssuesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapIssueEndpoints();
        return app;
    }
}
