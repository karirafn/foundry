using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Domain.Services;
using Foundry.Modules.Monitoring.Features.AccountTokenChecks;
using Foundry.Modules.Monitoring.Features.AccountTokenHealth;
using Foundry.Modules.Monitoring.Features.AwaitingInput;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Rotation;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.IssueReactions;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;
using Foundry.Modules.Monitoring.Features.PackageSourceProxy;
using Foundry.Modules.Monitoring.Features.PackageSources;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Modules.Monitoring.Features.Merge;
using Foundry.Modules.Monitoring.Features.Rebase;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Providers.Publishing;
using Foundry.Modules.Monitoring.Features.Providers.TrackerReads;
using Foundry.Modules.Monitoring.Features.RateBudget;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Modules.Monitoring.Features.SpecRewrite;
using Foundry.Modules.Monitoring.Features.TriageFailed;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Modules.Monitoring.Features.Webhooks.Delivery;
using Foundry.Modules.Monitoring.Features.Webhooks.Ingress;
using Foundry.Modules.Monitoring.Features.Webhooks.Nudges;
using Foundry.Modules.Monitoring.Features.Webhooks.Registration;
using Foundry.Modules.Monitoring.Features.Webhooks.StatusBroadcast;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Modules.Settings.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Http.Ingress;

using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        // Package source requests follow redirects themselves so the credential only reaches the source origin.
        services.AddHttpClient<PackageSourceUpstreamClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddTransient<IPackageSourceProber, PackageSourceProber>();
        services.AddScoped<IPackageSourceVerdicts, PackageSourceVerdicts>();
        services.AddScoped<PackageSourceVerdictPublisher>();
        services.AddScoped<PackageSourceProbing>();
        services.AddDomainEventHandler<PackageSourceReadabilityChanged, PackageSourceReadabilityChangedHandler>();
        services.TryAddSingleton<PackageSourceProbeGate>();
        services.TryAddSingleton<PackageSourceProbeRequests>();
        services.AddHostedService<PackageSourceProbeService>();
        services.AddScoped<GetPackageSources.Handler>();
        services.AddScoped<PackageSourceCredentialCommands.Handler>();
        services.AddHttpClient<GitLabHttpClient>(client =>
            client.MaxResponseContentBufferSize = MaxResponseContentBufferSize);

        services.AddScoped<INamespaceDeriver, NamespaceDeriver>();
        services.AddScoped<RepositoryEligibilityDiffer>();
        services.AddScoped<CredentialRotationService>();
        services.AddScoped<TokenAccountResolver>();
        services.AddScoped<IIssueProviderFactory, IssueProviderFactory>();
        services.AddScoped<ICredentialResolver, CredentialResolver>();
        services.AddScoped<IAccountTokenValidator, AccountTokenValidator>();
        services.AddScoped<AccountTokenVerdicts>();
        services.AddHostedService<AccountTokenExpiryBackfillService>();
        services.AddScoped<IAccountTokenHealthQueries, AccountTokenHealthQueries>();
        services.AddDomainEventHandler<CredentialTokenInvalidated, AccountTokenBroadcastHandler>();
        services.AddDomainEventHandler<CredentialTokenRevalidated, AccountTokenBroadcastHandler>();
        services.AddScoped<AccountTokenExpiryBroadcaster>();
        services.AddDomainEventHandler<CredentialTokenInvalidated, AccountTokenExpiryBroadcastHandler>();
        services.AddDomainEventHandler<CredentialTokenRevalidated, AccountTokenExpiryBroadcastHandler>();
        services.AddDomainEventHandler<CredentialTokenSet, AccountTokenExpiryBroadcastHandler>();
        services.AddDomainEventHandler<CredentialDeleted, AccountTokenExpiryBroadcastHandler>();
        services.AddHostedService<AccountTokenExpiryAlertService>();
        services.AddScoped<IRepositoryDispatchQueries, RepositoryDispatchQueries>();
        services.AddScoped<IHostRepositoryQueries, HostRepositoryQueries>();
        services.AddScoped<IRepositorySlugQueries, RepositorySlugQueries>();
        services.AddScoped<IRepositoryLifecycleSettingsQueries, RepositoryLifecycleSettingsQueries>();
        services.AddScoped<IRepositoryRemovalQueries, RepositoryRemovalQueries>();
        services.AddScoped<IRepositoryEligibilityQuery, RepositoryEligibilityQuery>();
        services.AddScoped<IRepositoryEligibilityEvaluator, RepositoryEligibilityEvaluator>();
        services.AddScoped<BranchRuleEnforcer>();
        services.AddScoped<OptedInBranchRuleApplier>();
        services.AddScoped<IPostExitProviderQueries, PostExitProviderQueries>();
        services.AddScoped<IWorkBranchCommands, PostExitProviderQueries>();
        services.AddScoped<IIssueBlockerQueries, PostExitProviderQueries>();
        services.AddScoped<IFailingJobLogQueries, PostExitProviderQueries>();
        services.AddScoped<IIssueContentQueries, PostExitProviderQueries>();
        services.AddScoped<ITrackerContextQueries, TrackerContextReader>();
        services.AddScoped<IRunPublishingCommands, RunPublishingCommands>();
        services.AddScoped<BlockerLinkWriter>();
        services.AddScoped<RepositoryPoller>();
        services.AddScoped<RebaseStep>();
        services.AddScoped<MergeStep>();
        services.AddScoped<IMergeQueueQueries>(sp => new MergeQueueQueries(
            sp.GetRequiredService<DbContext>(),
            () => sp.GetRequiredService<IIssueQueries>()));
        services.AddScoped<AwaitingInputStep>();
        services.AddScoped<TriageFailedEditStep>();
        services.AddScoped<SpecRewriteStep>();

        services.AddQueryHandler<GetAccounts.Query, IReadOnlyList<CredentialSummary>, GetAccounts.Handler>();
        services.AddQueryHandler<GetTokenRequirements.Query, TokenRequirements, GetTokenRequirements.Handler>();
        services.AddScoped<CreateAccount.Handler>();
        services.AddScoped<ICommandValidator<CreateAccount.Command>, CreateAccount.Validator>();
        services.AddScoped<UpdateAccount.Handler>();
        services.AddScoped<ICommandValidator<UpdateAccount.Command>, UpdateAccount.Validator>();
        services.AddCommandHandler<DeleteAccount.Command, bool, DeleteAccount.Handler>();
        services.AddQueryHandler<ValidateToken.Query, ValidateToken.Response, ValidateToken.Handler>();

        services.AddQueryHandler<GetRepositories.Query, IReadOnlyList<RepositorySummary>, GetRepositories.Handler>();
        services.AddQueryHandler<GetUnclaimedRepositories.Query, IReadOnlyList<UnclaimedRepositorySummary>, GetUnclaimedRepositories.Handler>();
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
        services.AddIngressEntry(static sp =>
            new IngressEntry(sp.GetRequiredService<IOptions<WebhookIngressOptions>>().Value.Port, WebhookIngressOptions.PathPrefix));
        // Package Source Proxy (ADR 0112): always on, on its own port, which serves only /package-sources.
        services.AddOptions<PackageSourceProxyOptions>().BindConfiguration(PackageSourceProxyOptions.SectionName);
        services.AddIngressEntry(static sp =>
            new IngressEntry(sp.GetRequiredService<IOptions<PackageSourceProxyOptions>>().Value.ProxyPort, PackageSourceProxyOptions.PathPrefix));
        services.AddSingleton<IPackageSourceProxyAddress, PackageSourceProxyAddress>();
        services.AddScoped<ProxyPackageSourceRequest.Handler>();
        services.TryAddSingleton<WebhookNudgeQueue>();
        services.AddScoped<WebhookIngress>();
        services.AddScoped<ReceiveGitHubWebhook.Handler>();
        services.AddScoped<ReceiveGitLabWebhook.Handler>();
        services.AddScoped<IWebhookStatusQueries, WebhookStatusQueries>();
        services.AddScoped<RepositoryWebhookEndpoints.Handler>();
        services.AddScoped<WebhookRegistrar>();
        services.AddScoped<RepositoryWebhookDeleter>();
        services.AddScoped<RepositoryRemoval>();
        services.AddScoped<UnclaimedRepositories>();
        services.AddScoped<ClaimRecheck>();
        services.AddHostedService<UnclaimedRepositoryReclaimService>();
        services.AddScoped<IDomainEventHandler<MonitoredRepositoryRemovedDomainEvent>, MonitoredRepositoryRemovedBridge>();
        services.AddIntegrationEventHandler<WebhookPublicBaseUrlChanged, WebhookPublicBaseUrlChangedHandler>();
        services.AddIntegrationEventHandler<WebhookPublicBaseUrlChanged, WebhookPublicBaseUrlChangedBroadcastHandler>();
        services.AddScoped<WebhookStatusBroadcastInterceptor>();
        // The self-probe (ADR 0116) sends exactly one request: the default resilience handler (retries and its own
        // timeouts) is cleared, redirects are not followed, and the probe applies its own 10 s timeout.
        services.AddHttpClient(WebhookIngressProbe.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler { AllowAutoRedirect = false })
            .ConfigureAdditionalHttpMessageHandlers(static (handlers, _) => handlers.Clear());
        services.TryAddSingleton<WebhookIngressProbe>();
        services.TryAddSingleton<IWebhookIngressProbe>(static sp => sp.GetRequiredService<WebhookIngressProbe>());
        services.AddIntegrationEventHandler<IssueEnteredChecksPending, IssueEnteredChecksPendingHandler>();
        services.AddIntegrationEventHandler<IssueEnteredRebasePending, IssueEnteredRebasePendingHandler>();
        services.AddIntegrationEventHandler<IssueEnteredMergePending, IssueEnteredMergePendingHandler>();
        services.AddIntegrationEventHandler<IssueEnteredAwaitingInput, IssueEnteredAwaitingInputHandler>();
        services.AddIntegrationEventHandler<TriageBlockersFound, TriageBlockersFoundHandler>();
        services.AddIntegrationEventHandler<TriageDecisionsRecorded, TriageDecisionsRecordedHandler>();

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
        app.MapPackageSourceEndpoints();
        ReceiveGitHubWebhook.Endpoint.Map(app);
        ReceiveGitLabWebhook.Endpoint.Map(app);
        ReceiveWebhookProbe.Endpoint.Map(app);
        ProxyPackageSourceRequest.Endpoint.Map(app);
        return app;
    }
}
