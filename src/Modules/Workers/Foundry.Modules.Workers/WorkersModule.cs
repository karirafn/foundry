using Docker.DotNet;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddSingleton<DockerClient>(_ =>
        {
            using DockerClientConfiguration config = new();
            return config.CreateClient();
        });
        services.AddSingleton<IImageOperations>(sp => sp.GetRequiredService<DockerClient>().Images);
        services.AddSingleton<IContainerOperations>(sp => sp.GetRequiredService<DockerClient>().Containers);
        services.AddSingleton<IWorkerOrchestrator, DockerWorkerOrchestrator>();
        services.AddSingleton<IContainerOutputParser, ContainerOutputParser>();
        services.AddSingleton<IWorkerImageRebuildQueue, WorkerImageRebuildQueue>();

        services.AddIntegrationEventHandler<IssueClaimed, IssueClaimedHandler>();
        services.AddIntegrationEventHandler<WorkerImageConfigurationChanged, WorkerImageConfigurationChangedHandler>();
        services.AddIntegrationEventHandler<DispatchPaused, DispatchPausedBroadcastHandler>();
        services.AddIntegrationEventHandler<DispatchResumed, DispatchResumedBroadcastHandler>();

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
