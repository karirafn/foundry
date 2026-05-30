using Foundry.Modules.Issues.Contracts;
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

        return services;
    }

    public static IEndpointRouteBuilder MapIssuesEndpoints(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
