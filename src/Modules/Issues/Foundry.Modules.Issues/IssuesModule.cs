using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.AwaitingInput;
using Foundry.Modules.Issues.Features.BranchDivergence;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Issues.Features.CredentialReactions;
using Foundry.Modules.Issues.Features.Dependencies;
using Foundry.Modules.Issues.Features.DispatchReactions;
using Foundry.Modules.Issues.Features.ProviderReactions;
using Foundry.Modules.Issues.Features.StateChanges;
using Foundry.Modules.Issues.Features.TransientRetry;
using Foundry.Modules.Issues.Features.WorkerReactions;
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
        services.AddScoped<InFlightWorkerCountQuery>();
        services.AddScoped<DispatchCandidateSelector>();
        services.AddScoped<IssueClaimer>();
        services.AddScoped<IAwaitingInputCommands, AwaitingInputCommands>();
        services.AddScoped<IssueDependencyApplier>();
        services.AddHostedService<TransientRetryService>();
        services.AddHostedService<BranchDivergedResumeService>();

        services.AddCommandHandler<RetryIssue.Command, IssueDetail, RetryIssue.Handler>();
        services.AddCommandHandler<RetryIssueFresh.Command, IssueDetail, RetryIssueFresh.Handler>();
        services.AddCommandHandler<RetryTriage.Command, IssueDetail, RetryTriage.Handler>();
        services.AddCommandHandler<SkipTriage.Command, IssueDetail, SkipTriage.Handler>();
        services.AddCommandHandler<Retriage.Command, IssueDetail, Retriage.Handler>();

        services.AddIntegrationEventHandler<IssueDetected, CreateIssueHandler>();
        services.AddIntegrationEventHandler<IssueDetailsChanged, UpdateIssueDetailsHandler>();
        services.AddIntegrationEventHandler<IssueDependenciesDetected, ProcessIssueDependenciesHandler>();
        services.AddIntegrationEventHandler<BlockerLinkFailed, BlockerLinkFailedHandler>();
        services.AddIntegrationEventHandler<WorkerCapacityAvailable, WorkerCapacityAvailableHandler>();
        services.AddIntegrationEventHandler<WorkerRunCompleted, WorkerRunCompletedHandler>();
        services.AddIntegrationEventHandler<WorkerRunFailed, WorkerRunFailedHandler>();
        services.AddIntegrationEventHandler<WorkerRunPublishRetryRequested, WorkerRunPublishRetryRequestedHandler>();
        services.AddIntegrationEventHandler<WorkerRunPublishBlocked, WorkerRunPublishBlockedHandler>();
        services.AddIntegrationEventHandler<WorkerRunPublishPhaseChanged, WorkerRunPublishPhaseChangedHandler>();
        services.AddIntegrationEventHandler<TriageRunCompleted, TriageRunCompletedHandler>();
        services.AddIntegrationEventHandler<ProviderIssueClosed, ProviderIssueClosedHandler>();
        services.AddIntegrationEventHandler<ProviderIssueUntracked, ProviderIssueUntrackedHandler>();
        services.AddIntegrationEventHandler<ProviderPullRequestClosed, ProviderPullRequestClosedHandler>();
        services.AddIntegrationEventHandler<PullRequestChangesRequested, PullRequestChangesRequestedHandler>();
        services.AddIntegrationEventHandler<ChecksVerdictResolved, ChecksVerdictResolvedHandler>();
        services.AddIntegrationEventHandler<RebaseResolved, RebaseResolvedHandler>();
        services.AddIntegrationEventHandler<AwaitingInputArrived, AwaitingInputArrivedHandler>();
        services.AddIntegrationEventHandler<DispatchResumed, DispatchResumedHandler>();
        services.AddIntegrationEventHandler<CredentialsValidated, CredentialsValidatedHandler>();
        services.AddIntegrationEventHandler<CreditsRestored, CreditsRestoredHandler>();

        services.AddScoped<IssueStateChangedHandler>();
        AddIssueStateChangedHandler<IssueQueued>(services);
        AddIssueStateChangedHandler<IssueBlocked>(services);
        AddIssueStateChangedHandler<IssueTriageBlocked>(services);
        AddIssueStateChangedHandler<IssueCompleted>(services);
        AddIssueStateChangedHandler<IssueFailed>(services);
        AddIssueStateChangedHandler<IssueInReview>(services);
        AddIssueStateChangedHandler<IssueUnchanged>(services);
        AddIssueStateChangedHandler<IssueRevisionQueued>(services);
        AddIssueStateChangedHandler<IssueRevisionFailed>(services);
        AddIssueStateChangedHandler<IssueInProgress>(services);
        AddIssueStateChangedHandler<IssueRevisionInProgress>(services);
        AddIssueStateChangedHandler<IssueContinuableFailed>(services);
        AddIssueStateChangedHandler<IssueContinuationQueued>(services);
        AddIssueStateChangedHandler<IssueChecksPending>(services);
        services.AddScoped<IDomainEventHandler<IssueChecksPending>, IssueChecksPendingNudgeBridge>();
        AddIssueStateChangedHandler<IssueRebasePending>(services);
        AddIssueStateChangedHandler<IssueAwaitingInput>(services);
        AddIssueStateChangedHandler<IssueTriageQueued>(services);
        AddIssueStateChangedHandler<IssueTriageInProgress>(services);
        AddIssueStateChangedHandler<IssueTriageFailed>(services);
        AddIssueStateChangedHandler<IssuePublishFailed>(services);
        AddIssueStateChangedHandler<IssuePublishBlocked>(services);
        AddIssueStateChangedHandler<IssueBranchDiverged>(services);
        services.AddScoped<IDomainEventHandler<IssueAwaitingInput>, IssueAwaitingInputNudgeBridge>();
        services.AddScoped<IDomainEventHandler<IssueRebasePending>, IssueRebasePendingNudgeBridge>();
        services.AddScoped<IDomainEventHandler<TriageBlockersNamed>, TriageBlockersFoundBridge>();

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
