using Docker.DotNet;

using Foundry.WebApi.Modules.Workers.Features;
using Foundry.WebApi.Modules.Workers.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.WebApi.Modules.Workers;

public static class WorkersModule
{
    public static IServiceCollection AddWorkersModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<WorkerOptions>(configuration.GetSection("Workers"));

        services.AddSingleton<DockerClient>(_ => new DockerClientConfiguration().CreateClient());
        services.AddSingleton<IWorkerOrchestrator, DockerWorkerOrchestrator>();

        services.AddHostedService<WorkerDispatchService>();

        return services;
    }
}
