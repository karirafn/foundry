using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.DockerAvailability;
using Foundry.Modules.Workers.Features.Health;
using Foundry.Modules.Workers.Features.ImageBuild;
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
    public static IServiceCollection AddWorkersModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<WorkerOptions>(configuration.GetSection("Workers"));
        services.AddSingleton<IValidateOptions<WorkerOptions>, WorkerOptionsValidator>();

        services.AddScoped<IWorkerRunQueries, WorkerRunQueries>();
        services.AddScoped<IWorkerLogStream, WorkerLogStream>();

        services.AddSharedDockerInfrastructure();

        services.AddHealthChecks()
            .AddCheck<DockerDaemonHealthCheck>("docker-daemon", tags: ["ready"]);
        services.AddSingleton<IHealthCheckPublisher, DockerAvailabilityHealthCheckPublisher>();
        services.AddSingleton<IWorkerOrchestrator, DockerWorkerOrchestrator>();
        services.AddSingleton<IContainerOutputParser, ContainerOutputParser>();
        services.AddSingleton<IWorkerImageRebuildQueue, WorkerImageRebuildQueue>();

        services.AddIntegrationEventHandler<IssueClaimed, IssueClaimedHandler>();
        services.AddIntegrationEventHandler<WorkerImageConfigurationChanged, WorkerImageConfigurationChangedHandler>();
        services.AddIntegrationEventHandler<DispatchPaused, DispatchPausedBroadcastHandler>();
        services.AddIntegrationEventHandler<DispatchResumed, DispatchResumedBroadcastHandler>();
        services.AddDomainEventHandler<WorkerActivityObserved, WorkerActivityObservedHandler>();

        services.AddScoped<WorkerOutcomeResolver>();
        services.AddHostedService<WorkerDispatchService>();
        services.AddHostedService<WorkerImageRebuildService>();

        return services;
    }

    public static IEndpointRouteBuilder MapWorkersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapWorkerEndpoints();
        return app;
    }
}
