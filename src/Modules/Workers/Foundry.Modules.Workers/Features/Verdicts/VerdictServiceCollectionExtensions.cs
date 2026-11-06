using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Modules.Workers.Features.Verdicts;

internal static class VerdictServiceCollectionExtensions
{
    /// <summary>Registers the typed verdict parser of every role that has one, and the reader that picks between them.</summary>
    public static IServiceCollection AddWorkerVerdicts(this IServiceCollection services)
    {
        services.AddSingleton<IWorkerVerdictParser, ImplementVerdictParser>();
        services.AddSingleton<RunVerdictReader>();
        return services;
    }
}
