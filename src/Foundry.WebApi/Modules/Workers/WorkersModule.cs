using Docker.DotNet;

using Foundry.Modules.Issues.Contracts;
using Foundry.Shared.Infrastructure;
using Foundry.WebApi.Modules.Workers.Features;
using Foundry.WebApi.Modules.Workers.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Foundry.WebApi.Modules.Workers;

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
        services.AddSingleton<IWorkerOrchestrator, DockerWorkerOrchestrator>();

        services.AddIntegrationEventHandler<IssueClaimed, IssueClaimedHandler>();

        services.AddHostedService<WorkerDispatchService>();

        return services;
    }
}
