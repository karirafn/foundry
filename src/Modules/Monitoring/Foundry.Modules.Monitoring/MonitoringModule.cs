using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Modules.Monitoring;

public static class MonitoringModule
{
    public static IServiceCollection AddMonitoringModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MonitoringOptions>(configuration.GetSection("Monitoring"));

        services.AddHttpClient<GitHubHttpClient>();

        services.AddScoped<IIssueProviderFactory, IssueProviderFactory>();
        services.AddScoped<IRepositoryDispatchQueries, RepositoryDispatchQueries>();
        services.AddScoped<IRepositorySlugQueries, RepositorySlugQueries>();
        services.AddScoped<IBranchProtectionValidator, BranchProtectionValidator>();
        services.AddScoped<RepositoryPoller>();

        services.AddQueryHandler<GetAccounts.Query, IReadOnlyList<AccountSummary>, GetAccounts.Handler>();
        services.AddCommandHandler<CreateAccount.Command, AccountSummary, CreateAccount.Handler, CreateAccount.Validator>();
        services.AddCommandHandler<UpdateAccount.Command, AccountSummary, UpdateAccount.Handler, UpdateAccount.Validator>();
        services.AddCommandHandler<DeleteAccount.Command, bool, DeleteAccount.Handler>();
        services.AddQueryHandler<ValidateToken.Query, ValidateToken.Response, ValidateToken.Handler>();

        services.AddQueryHandler<GetRepositories.Query, IReadOnlyList<RepositorySummary>, GetRepositories.Handler>();
        services.AddCommandHandler<CreateRepository.Command, RepositorySummary, CreateRepository.Handler, CreateRepository.Validator>();
        services.AddCommandHandler<UpdateRepository.Command, RepositorySummary, UpdateRepository.Handler, UpdateRepository.Validator>();

        services.AddHostedService<MonitoringService>();

        return services;
    }

    public static IEndpointRouteBuilder MapMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAccountEndpoints();
        app.MapRepositoryEndpoints();
        return app;
    }
}
