using Docker.DotNet;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Foundry.Shared.Infrastructure.Docker;

public static class DockerServiceCollectionExtensions
{
    public static IServiceCollection AddSharedDockerInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<DockerClient>(_ =>
        {
            using DockerClientConfiguration config = new();
            return config.CreateClient();
        });

        services.TryAddSingleton<IImageOperations>(sp => sp.GetRequiredService<DockerClient>().Images);
        services.TryAddSingleton<IContainerOperations>(sp => sp.GetRequiredService<DockerClient>().Containers);
        services.TryAddSingleton<IVolumeOperations>(sp => sp.GetRequiredService<DockerClient>().Volumes);
        services.TryAddSingleton<IExecOperations>(sp => sp.GetRequiredService<DockerClient>().Exec);
        services.TryAddSingleton<ISystemOperations>(sp => sp.GetRequiredService<DockerClient>().System);

        services.TryAddSingleton<IDockerContainerRuntime, DockerContainerRuntime>();

        return services;
    }
}
