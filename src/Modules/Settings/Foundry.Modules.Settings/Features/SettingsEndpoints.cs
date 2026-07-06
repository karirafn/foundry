using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Settings.Features;

internal static class SettingsEndpoints
{
    internal static IEndpointRouteBuilder MapSettingEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/settings")
            .WithTags("Settings");

        GetSettings.Endpoint.Map(group);
        UpdateWorkerLimits.Endpoint.Map(group);
        UpdatePromptTemplates.Endpoint.Map(group);
        UpdateDispatchSettings.Endpoint.Map(group);
        PauseDispatch.Endpoint.Map(group);
        ResumeDispatch.Endpoint.Map(group);
        UpdateWorkerImageConfiguration.Endpoint.Map(group);
        RetryImageBuild.Endpoint.Map(group);

        return routes;
    }
}
