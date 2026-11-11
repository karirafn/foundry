using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Modules.Workers.Features.Activity;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.DockerAvailability;
using Foundry.Modules.Workers.Features.Health;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Modules.Workers.Features.Roles.Models;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Docker;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Foundry.Modules.Workers;

public static class WorkersModule
{
    // This period is process-global — all health-check publishers ride this cadence.
    private const int HealthCheckPublisherPeriodSeconds = 15;
    private const int ModelsApiTimeoutSeconds = 30;
    private const int ModelsApiMaxResponseBytes = 4 * 1024 * 1024;

    public static IServiceCollection AddWorkersModule(this IServiceCollection services)
    {
        services.AddScoped<IWorkerRunQueries, WorkerRunQueries>();
        services.AddScoped<IRunningBranchQueries, RunningBranchQueries>();
        services.AddScoped<IWorkerLogStream, WorkerLogStream>();
        services.AddCommandHandler<RequestRunCancellation.Command, RequestRunCancellation.Response, RequestRunCancellation.Handler>();
        services.AddCommandHandler<RequestPublishRetry.Command, RequestPublishRetry.Response, RequestPublishRetry.Handler>();
        services.AddSingleton<AgentActivityStore>();
        services.AddSingleton<IAgentActivityQueries>(sp => sp.GetRequiredService<AgentActivityStore>());

        services.AddSharedDockerInfrastructure();

        services.AddSingleton<DockerAvailabilityState>();
        services.AddSingleton<IDockerAvailabilityState>(sp => sp.GetRequiredService<DockerAvailabilityState>());
        services.AddSingleton<IDockerAvailabilityStateMutator>(sp => sp.GetRequiredService<DockerAvailabilityState>());

        services.AddHealthChecks()
            .AddCheck<DockerDaemonHealthCheck>(DockerDaemonHealthCheck.CheckName, tags: ["ready"]);
        services.AddSingleton<IHealthCheckPublisher, DockerAvailabilityHealthCheckPublisher>();
        services.Configure<HealthCheckPublisherOptions>(options =>
            options.Period = TimeSpan.FromSeconds(HealthCheckPublisherPeriodSeconds));
        services.AddIntegrationEventHandler<DockerAvailabilityChanged, DockerAvailabilityChangedBroadcastHandler>();
        services.AddSingleton<IWorkerOrchestrator, DockerWorkerOrchestrator>();
        services.AddSingleton<IPublisher, DockerPublisher>();
        services.AddSingleton<IContainerOutputParser, ContainerOutputParser>();
        services.AddWorkerVerdicts();
        services.AddSingleton<IProbeOutcomeClassifier, ProbeOutcomeClassifier>();
        services.AddSingleton<IWorkerImageRebuildQueue, WorkerImageRebuildQueue>();
        services.AddSingleton<IWorkerRoleCatalogStore, WorkerRoleCatalogStore>();
        services.AddHttpClient<IClaudeModelsApi, AnthropicModelsClient>(client =>
        {
            client.BaseAddress = AnthropicModelsClient.BaseAddress;
            client.Timeout = TimeSpan.FromSeconds(ModelsApiTimeoutSeconds);
            client.MaxResponseContentBufferSize = ModelsApiMaxResponseBytes;
        });
        services.AddSingleton<IClaudeModelCatalog, ClaudeModelCatalog>();
        services.AddCommandHandler<UpdateWorkerRoleModel.Command, WorkerRoleModelResponse, UpdateWorkerRoleModel.Handler>();

        services.AddIntegrationEventHandler<IssueClaimed, IssueClaimedHandler>();
        services.AddIntegrationEventHandler<ClaimSkipped, ClaimSkippedHandler>();
        services.AddIntegrationEventHandler<WorkerImageSettingsChanged, WorkerImageSettingsChangedHandler>();
        services.AddIntegrationEventHandler<DispatchPaused, DispatchPausedBroadcastHandler>();
        services.AddIntegrationEventHandler<DispatchResumed, DispatchResumedBroadcastHandler>();
        services.AddIntegrationEventHandler<ImageBuildStarted, ImageBuildStartedBroadcastHandler>();
        services.AddIntegrationEventHandler<ImageBuildCompleted, ImageBuildCompletedBroadcastHandler>();
        services.AddIntegrationEventHandler<ImageBuildFailed, ImageBuildFailedBroadcastHandler>();
        services.AddDomainEventHandler<WorkerActivityObserved, WorkerActivityObservedHandler>();
        services.AddDomainEventHandler<Domain.Events.WorkerRunFailed, WorkerRunFailedBridgeHandler>();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<WorkerOutcomeResolver>();
        services.AddScoped<WorkerOutcomeApplier>();
        services.AddScoped<WorkerRunMonitor>();
        services.AddHostedService<WorkerDispatchService>();
        services.AddHostedService<AgentActivityFollowerService>();
        services.AddHostedService<WorkerImageRebuildService>();
        services.AddHostedService<StaleStartingRunService>();
        services.AddHostedService<StaleReservationService>();
        services.AddHostedService<RunTranscriptRetentionService>();

        return services;
    }

    public static IEndpointRouteBuilder MapWorkersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapWorkerEndpoints();
        app.MapSystemEndpoints();
        app.MapWorkerRoleEndpoints();
        return app;
    }
}
