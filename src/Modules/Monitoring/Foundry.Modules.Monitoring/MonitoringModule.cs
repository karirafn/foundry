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
        services.AddHttpClient<GitLabHttpClient>();

        services.AddScoped<INamespaceDeriver, NamespaceDeriver>();
        services.AddScoped<RepositoryEligibilityDiffer>();
        services.AddScoped<CredentialRotationService>();
        services.AddScoped<IIssueProviderFactory, IssueProviderFactory>();
        services.AddScoped<ICredentialResolver, CredentialResolver>();
        services.AddScoped<IRepositoryDispatchQueries, RepositoryDispatchQueries>();
        services.AddScoped<IRepositorySlugQueries, RepositorySlugQueries>();
        services.AddScoped<IRepositoryEligibilityQuery, RepositoryEligibilityQuery>();
        services.AddScoped<IRepositoryEligibilityEvaluator, RepositoryEligibilityEvaluator>();
        services.AddScoped<IPostExitProviderQueries, PostExitProviderQueries>();
        services.AddScoped<RepositoryPoller>();

        services.AddQueryHandler<GetAccounts.Query, IReadOnlyList<CredentialSummary>, GetAccounts.Handler>();
        services.AddQueryHandler<GetTokenRequirements.Query, TokenRequirements, GetTokenRequirements.Handler>();
        services.AddScoped<CreateAccount.Handler>();
        services.AddScoped<ICommandValidator<CreateAccount.Command>, CreateAccount.Validator>();
        services.AddCommandHandler<UpdateAccount.Command, CredentialUpdateResult, UpdateAccount.Handler, UpdateAccount.Validator>();
        services.AddCommandHandler<DeleteAccount.Command, bool, DeleteAccount.Handler>();
        services.AddQueryHandler<ValidateToken.Query, ValidateToken.Response, ValidateToken.Handler>();

        services.AddQueryHandler<GetRepositories.Query, IReadOnlyList<RepositorySummary>, GetRepositories.Handler>();
        services.AddQueryHandler<GetAvailableRepositories.Query, IReadOnlyList<ProviderRepository>, GetAvailableRepositories.Handler>();
        services.AddCommandHandler<CreateRepository.Command, RepositorySummary, CreateRepository.Handler, CreateRepository.Validator>();
        services.AddCommandHandler<UpdateRepository.Command, RepositorySummary, UpdateRepository.Handler, UpdateRepository.Validator>();
        services.AddCommandHandler<DeleteRepository.Command, bool, DeleteRepository.Handler>();
        services.AddCommandHandler<MoveRepository.Command, bool, MoveRepository.Handler, MoveRepository.Validator>();
        services.AddCommandHandler<RecheckRepositoryEligibility.Command, RepositorySummary, RecheckRepositoryEligibility.Handler>();

        services.AddHostedService<MonitoringService>();

        return services;
    }

    public static IEndpointRouteBuilder MapMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAccountEndpoints();
        app.MapProviderEndpoints();
        app.MapRepositoryEndpoints();
        return app;
    }
}
