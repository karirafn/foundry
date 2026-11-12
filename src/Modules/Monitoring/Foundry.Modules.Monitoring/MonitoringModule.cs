using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Services;
using Foundry.Modules.Monitoring.Features.AwaitingInput;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Rotation;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Modules.Monitoring.Features.Rebase;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Providers.Publishing;
using Foundry.Modules.Monitoring.Features.RateBudget;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Modules.Settings.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring;

public static class MonitoringModule
{
    // GitLab's REST issues API has no field selection and returns the full issue description
    // regardless of what Foundry requests. Cap at 50 MiB to stop only catastrophic payloads
    // (e.g. a description containing megabytes of base64-encoded content) while clearing any
    // legitimate issue page.
    internal const long MaxResponseContentBufferSize = 50 * 1024 * 1024;

    public static IServiceCollection AddMonitoringModule(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.TryAddSingleton<DefaultBranchCache>();
        services.TryAddSingleton<IHostAddressResolver, SystemHostAddressResolver>();
        services.TryAddSingleton<IProviderRateBudget, InMemoryProviderRateBudget>();
        services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);
        services.TryAddSingleton<ActionableFeedbackPolicy>();
        services.AddScoped<ProviderHostGuard>();
        services.AddScoped<PostedCommentRegistry>();

        services.AddHttpClient<GitHubHttpClient>(client =>
            client.MaxResponseContentBufferSize = MaxResponseContentBufferSize);
        services.AddTransient<IGitHubWriteProber>(sp => sp.GetRequiredService<GitHubHttpClient>());
        // Job-log downloads follow GitHub's redirect themselves so the token never reaches the storage host.
        services.AddHttpClient<GitHubJobLogClient>(client =>
                client.MaxResponseContentBufferSize = MaxResponseContentBufferSize)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddHttpClient<GitLabHttpClient>(client =>
            client.MaxResponseContentBufferSize = MaxResponseContentBufferSize);

        services.AddScoped<INamespaceDeriver, NamespaceDeriver>();
        services.AddScoped<RepositoryEligibilityDiffer>();
        services.AddScoped<CredentialRotationService>();
        services.AddScoped<TokenAccountResolver>();
        services.AddScoped<IIssueProviderFactory, IssueProviderFactory>();
        services.AddScoped<ICredentialResolver, CredentialResolver>();
        services.AddScoped<IRepositoryDispatchQueries, RepositoryDispatchQueries>();
        services.AddScoped<IRepositorySlugQueries, RepositorySlugQueries>();
        services.AddScoped<IRepositoryEligibilityQuery, RepositoryEligibilityQuery>();
        services.AddScoped<IRepositoryEligibilityEvaluator, RepositoryEligibilityEvaluator>();
        services.AddScoped<BranchRuleEnforcer>();
        services.AddScoped<OptedInBranchRuleApplier>();
        services.AddScoped<IPostExitProviderQueries, PostExitProviderQueries>();
        services.AddScoped<IWorkBranchCommands, PostExitProviderQueries>();
        services.AddScoped<IIssueBlockerQueries, PostExitProviderQueries>();
        services.AddScoped<IFailingJobLogQueries, PostExitProviderQueries>();
        services.AddScoped<IIssueContentQueries, PostExitProviderQueries>();
        services.AddScoped<IRunPublishingCommands, RunPublishingCommands>();
        services.AddScoped<RepositoryPoller>();
        services.AddScoped<RebaseStep>();
        services.AddScoped<AwaitingInputStep>();

        services.AddQueryHandler<GetAccounts.Query, IReadOnlyList<CredentialSummary>, GetAccounts.Handler>();
        services.AddQueryHandler<GetTokenRequirements.Query, TokenRequirements, GetTokenRequirements.Handler>();
        services.AddScoped<CreateAccount.Handler>();
        services.AddScoped<ICommandValidator<CreateAccount.Command>, CreateAccount.Validator>();
        services.AddScoped<UpdateAccount.Handler>();
        services.AddScoped<ICommandValidator<UpdateAccount.Command>, UpdateAccount.Validator>();
        services.AddCommandHandler<DeleteAccount.Command, bool, DeleteAccount.Handler>();
        services.AddQueryHandler<ValidateToken.Query, ValidateToken.Response, ValidateToken.Handler>();

        services.AddQueryHandler<GetRepositories.Query, IReadOnlyList<RepositorySummary>, GetRepositories.Handler>();
        services.AddQueryHandler<GetAvailableRepositories.Query, AvailableRepositoriesResponse, GetAvailableRepositories.Handler>();
        services.AddCommandHandler<CreateRepository.Command, CreateRepositoryResponse, CreateRepository.Handler, CreateRepository.Validator>();
        services.AddCommandHandler<UpdateRepository.Command, RepositorySummary, UpdateRepository.Handler>();
        services.AddCommandHandler<DeleteRepository.Command, bool, DeleteRepository.Handler>();
        services.AddCommandHandler<MoveRepository.Command, bool, MoveRepository.Handler, MoveRepository.Validator>();
        services.AddCommandHandler<RecheckRepositoryEligibility.Command, RepositorySummary, RecheckRepositoryEligibility.Handler>();
        services.AddCommandHandler<ApplyBranchRules.Command, ApplyBranchRulesResponse, ApplyBranchRules.Handler>();

        // Webhook ingress (ADR 0085): host configuration, the single-consumer nudge channel, the
        // receiving endpoint and the manual-registration endpoints.
        services.AddOptions<WebhookIngressOptions>().BindConfiguration(WebhookIngressOptions.SectionName);
        services.TryAddSingleton<WebhookNudgeQueue>();
        services.AddScoped<WebhookIngress>();
        services.AddScoped<ReceiveGitHubWebhook.Handler>();
        services.AddScoped<ReceiveGitLabWebhook.Handler>();
        services.AddScoped<BranchPushPublisher>();
        services.AddScoped<IWebhookStatusQueries, WebhookStatusQueries>();
        services.AddScoped<RepositoryWebhookEndpoints.Handler>();
        services.AddScoped<WebhookRegistrar>();
        services.AddScoped<AccountWebhookRegistrar>();
        services.AddIntegrationEventHandler<WebhookPublicBaseUrlChanged, WebhookPublicBaseUrlChangedHandler>();
        services.AddIntegrationEventHandler<IssueEnteredChecksPending, IssueEnteredChecksPendingHandler>();
        services.AddIntegrationEventHandler<IssueEnteredRebasePending, IssueEnteredRebasePendingHandler>();
        services.AddIntegrationEventHandler<IssueEnteredAwaitingInput, IssueEnteredAwaitingInputHandler>();

        services.AddHostedService(sp => new MonitoringService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<WebhookNudgeQueue>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<MonitoringService>>()));

        return services;
    }

    public static IEndpointRouteBuilder MapMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAccountEndpoints();
        app.MapProviderEndpoints();
        app.MapRateBudgetEndpoints();
        app.MapRepositoryEndpoints();
        ReceiveGitHubWebhook.Endpoint.Map(app);
        ReceiveGitLabWebhook.Endpoint.Map(app);
        return app;
    }
}
