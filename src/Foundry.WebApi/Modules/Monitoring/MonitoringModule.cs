using Foundry.WebApi.Modules.Monitoring.Features;
using Foundry.WebApi.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.WebApi.Shared.Infrastructure;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.WebApi.Modules.Monitoring;

public static class MonitoringModule
{
    public static IServiceCollection AddMonitoringModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MonitoringOptions>(configuration.GetSection("Monitoring"));

        services.AddHttpClient<GitHubHttpClient>();

        services.AddScoped<IIssueProviderFactory, IssueProviderFactory>();
        services.AddScoped<IProviderAuth, ConfigurationProviderAuth>();
        services.AddScoped<RepositoryPoller>();

        services.AddHostedService<MonitoringSeeder>();
        services.AddHostedService<MonitoringService>();

        return services;
    }

    public static IEndpointRouteBuilder MapMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
