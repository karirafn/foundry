using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.DockerAvailability;
using Foundry.Modules.Workers.Features.Health;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Docker;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers;

public static class WorkersModule
{
    // This period is process-global — all health-check publishers ride this cadence.
    private const int HealthCheckPublisherPeriodSeconds = 15;

    public static IServiceCollection AddWorkersModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<WorkerOptions>(configuration.GetSection("Workers"));
        services.AddSingleton<IValidateOptions<WorkerOptions>, WorkerOptionsValidator>();

        services.AddScoped<IWorkerRunQueries, WorkerRunQueries>();
        services.AddScoped<IWorkerLogStream, WorkerLogStream>();

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
        services.AddSingleton<IContainerOutputParser, ContainerOutputParser>();
        services.AddSingleton<IProbeOutcomeClassifier, ProbeOutcomeClassifier>();
        services.AddSingleton<IWorkerImageRebuildQueue, WorkerImageRebuildQueue>();

        services.AddIntegrationEventHandler<IssueClaimed, IssueClaimedHandler>();
        services.AddIntegrationEventHandler<ClaimSkipped, ClaimSkippedHandler>();
        services.AddIntegrationEventHandler<WorkerImageConfigurationChanged, WorkerImageConfigurationChangedHandler>();
        services.AddIntegrationEventHandler<DispatchPaused, DispatchPausedBroadcastHandler>();
        services.AddIntegrationEventHandler<DispatchResumed, DispatchResumedBroadcastHandler>();
        services.AddIntegrationEventHandler<ImageBuildStarted, ImageBuildStartedBroadcastHandler>();
        services.AddIntegrationEventHandler<ImageBuildCompleted, ImageBuildCompletedBroadcastHandler>();
        services.AddIntegrationEventHandler<ImageBuildFailed, ImageBuildFailedBroadcastHandler>();
        services.AddDomainEventHandler<WorkerActivityObserved, WorkerActivityObservedHandler>();
        services.AddDomainEventHandler<Domain.Events.WorkerRunFailed, WorkerRunFailedBridgeHandler>();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<WorkerOutcomeResolver>();
        services.AddHostedService<WorkerDispatchService>();
        services.AddHostedService<WorkerImageRebuildService>();
        services.AddHostedService<StaleStartingRunService>();
        services.AddHostedService<StaleReservationService>();

        return services;
    }

    public static IEndpointRouteBuilder MapWorkersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapWorkerEndpoints();
        app.MapSystemEndpoints();
        return app;
    }
}
