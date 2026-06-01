using Docker.DotNet;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features;
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
        services.AddSingleton<IWorkerOrchestrator, DockerWorkerOrchestrator>();

        services.AddIntegrationEventHandler<IssueClaimed, IssueClaimedHandler>();

        services.AddHostedService<WorkerDispatchService>();

        services.AddCommandHandler<IngestReport.Command, WorkerReportSummary, IngestReport.Handler>();
        services.AddQueryHandler<GetReports.Query, IReadOnlyList<WorkerReportSummary>, GetReports.Handler>();

        return services;
    }

    public static IEndpointRouteBuilder MapWorkersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapWorkerEndpoints();
        return app;
    }
}
