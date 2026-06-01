using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Modules.Monitoring;

public static class MonitoringModule
{
    public static IServiceCollection AddProviderAuth(this IServiceCollection services)
    {
        services.AddScoped<IProviderAuth, ConfigurationProviderAuth>();
        return services;
    }

    public static IServiceCollection AddMonitoringModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MonitoringOptions>(configuration.GetSection("Monitoring"));

        services.AddHttpClient<GitHubHttpClient>();

        services.AddScoped<IIssueProviderFactory, IssueProviderFactory>();
        services.AddScoped<IRepositoryDispatchQueries, RepositoryDispatchQueries>();
        services.AddScoped<IRepositorySlugQueries, RepositorySlugQueries>();
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
