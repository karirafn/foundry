using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Features;
using Foundry.Modules.Settings.Features.Dispatch;
using Foundry.Modules.Settings.Features.WorkerConfig;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Modules.Settings;

public static class SettingsModule
{
    public static IServiceCollection AddSettingsModule(this IServiceCollection services)
    {
        services.AddScoped<IGlobalSettingsQueries, GlobalSettingsQueries>();
        services.AddHostedService<SettingsSeeder>();

        services.AddQueryHandler<GetSettings.Query, GlobalSettingsSummary, GetSettings.Handler>();
        services.AddCommandHandler<UpdateWorkerLimits.Command, GlobalSettingsSummary, UpdateWorkerLimits.Handler, UpdateWorkerLimits.Validator>();
        services.AddCommandHandler<UpdatePromptTemplates.Command, GlobalSettingsSummary, UpdatePromptTemplates.Handler, UpdatePromptTemplates.Validator>();
        services.AddCommandHandler<UpdateDispatchSettings.Command, GlobalSettingsSummary, UpdateDispatchSettings.Handler>();
        services.AddCommandHandler<PauseDispatch.Command, GlobalSettingsSummary, PauseDispatch.Handler>();
        services.AddCommandHandler<ResumeDispatch.Command, GlobalSettingsSummary, ResumeDispatch.Handler>();
        services.AddCommandHandler<UpdateWorkerImageConfiguration.Command, GlobalSettingsSummary, UpdateWorkerImageConfiguration.Handler>();
        services.AddCommandHandler<RetryImageBuild.Command, GlobalSettingsSummary, RetryImageBuild.Handler>();

        services.AddIntegrationEventHandler<ImageBuildRequested, ImageBuildRequestedHandler>();
        services.AddIntegrationEventHandler<ImageBuildSucceeded, ImageBuildSucceededHandler>();
        services.AddIntegrationEventHandler<ImageBuildOutcomeFailed, ImageBuildOutcomeFailedHandler>();

        return services;
    }

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapSettingEndpoints();
        return app;
    }
}
