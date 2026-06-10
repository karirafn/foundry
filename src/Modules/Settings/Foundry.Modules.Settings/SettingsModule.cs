using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.Modules.Settings.Infrastructure;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Modules.Settings;

public static class SettingsModule
{
    public static IServiceCollection AddSettingsModule(this IServiceCollection services)
    {
        services.AddScoped<IGlobalSettingsQueries, GlobalSettingsQueries>();
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddScoped<IOAuthCredentialScanner, FileSystemOAuthCredentialScanner>();
        services.AddHostedService<SettingsSeeder>();

        services.AddQueryHandler<GetSettings.Query, GlobalSettingsSummary, GetSettings.Handler>();
        services.AddQueryHandler<ScanOAuthCredentials.Query, OAuthCredentials, ScanOAuthCredentials.Handler>();
        services.AddCommandHandler<UpdateAuthMode.Command, UpdateAuthMode.Response, UpdateAuthMode.Handler, UpdateAuthMode.Validator>();

        return services;
    }

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapSettingEndpoints();
        return app;
    }
}
