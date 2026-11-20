using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Features;
using Foundry.Modules.Settings.Features.AwaitingInput;
using Foundry.Modules.Settings.Features.Dispatch;
using Foundry.Modules.Settings.Features.ProviderHosts;
using Foundry.Modules.Settings.Features.Webhooks;
using Foundry.Modules.Settings.Features.WorkerConfig;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Modules.Settings;

public static class SettingsModule
{
    public static IServiceCollection AddSettingsModule(this IServiceCollection services)
    {
        services.AddScoped<IGlobalSettingsQueries, GlobalSettingsQueries>();
        services.AddScoped<IWebhookSettingsQueries, WebhookSettingsQueries>();
        services.AddHostedService<SettingsSeeder>();

        services.AddQueryHandler<GetSettings.Query, GlobalSettingsSummary, GetSettings.Handler>();
        services.AddCommandHandler<UpdateWorkerLimits.Command, GlobalSettingsSummary, UpdateWorkerLimits.Handler, UpdateWorkerLimits.Validator>();
        services.AddCommandHandler<UpdateDispatchSettings.Command, GlobalSettingsSummary, UpdateDispatchSettings.Handler, UpdateDispatchSettings.Validator>();
        services.AddCommandHandler<UpdateWorkerImageConfiguration.Command, GlobalSettingsSummary, UpdateWorkerImageConfiguration.Handler>();
        services.AddCommandHandler<UpdateNestedDocker.Command, GlobalSettingsSummary, UpdateNestedDocker.Handler>();
        services.AddCommandHandler<UpdateAwaitingInputSettings.Command, GlobalSettingsSummary, UpdateAwaitingInputSettings.Handler>();
        services.AddCommandHandler<UpdateWorkerEnvironment.Command, GlobalSettingsSummary, UpdateWorkerEnvironment.Handler>();
        services.AddCommandHandler<UpdateClaudeCodeSettings.Command, GlobalSettingsSummary, UpdateClaudeCodeSettings.Handler>();
        services.AddCommandHandler<RetryImageBuild.Command, GlobalSettingsSummary, RetryImageBuild.Handler>();
        services.AddCommandHandler<UpdateAllowedProviderHosts.Command, GlobalSettingsSummary, UpdateAllowedProviderHosts.Handler>();
        services.AddCommandHandler<UpdateWebhookSettings.Command, GlobalSettingsSummary, UpdateWebhookSettings.Handler>();

        services.AddIntegrationEventHandler<ImageBuildRequested, ImageBuildRequestedHandler>();
        services.AddIntegrationEventHandler<ImageBuildSucceeded, ImageBuildSucceededHandler>();
        services.AddIntegrationEventHandler<ImageBuildOutcomeFailed, ImageBuildOutcomeFailedHandler>();

        return services;
    }

    /// <summary>
    /// Maps the <c>/api/settings</c> routes. The dispatch pause and resume routes belong to the Workers module, which
    /// owns the pause state; <paramref name="mapDispatchPauseEndpoints"/> maps them into this group, at their place among
    /// the settings routes, so the routes, their tag and the OpenAPI document stay as they were.
    /// </summary>
    public static IEndpointRouteBuilder MapSettingsEndpoints(
        this IEndpointRouteBuilder app,
        Action<RouteGroupBuilder> mapDispatchPauseEndpoints)
    {
        app.MapSettingEndpoints(mapDispatchPauseEndpoints);
        return app;
    }
}
