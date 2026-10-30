using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Contracts.Queries;
using Foundry.Modules.Credentials.Features;
using Foundry.Modules.Credentials.Features.Broadcasts;
using Foundry.Modules.Credentials.Features.CredentialReactions;
using Foundry.Modules.Credentials.Features.CreditProbe;
using Foundry.Modules.Credentials.Features.DispatchReactions;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Modules.Credentials.Features.WorkerReactions;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Docker;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Foundry.Modules.Credentials;

public static class CredentialsModule
{
    public static IServiceCollection AddCredentialsModule(this IServiceCollection services)
    {
        services.AddQueryHandler<GetCredentials.Query, ClaudeAccountSummary, GetCredentials.Handler>();
        services.AddCommandHandler<UpdateAuthMode.Command, ClaudeAccountSummary, UpdateAuthMode.Handler, UpdateAuthMode.Validator>();
        services.AddHostedService<ClaudeAccountSeeder>();

        services.AddIntegrationEventHandler<DispatchResumed, DispatchResumedSpendHandler>();
        services.AddIntegrationEventHandler<WorkerAuthenticationFailed, WorkerAuthenticationFailedHandler>();
        services.AddIntegrationEventHandler<WorkerCreditsExhausted, WorkerCreditsExhaustedHandler>();
        services.AddIntegrationEventHandler<CredentialsInvalidated, CredentialsInvalidatedBroadcastHandler>();
        services.AddIntegrationEventHandler<CredentialsValidated, CredentialsValidatedBroadcastHandler>();
        services.AddIntegrationEventHandler<WorkerCreditsExhausted, WorkerCreditsExhaustedBroadcastHandler>();
        services.AddIntegrationEventHandler<CreditsRestored, CreditsRestoredBroadcastHandler>();

        // Usage event reactions — a second handler for the same event (multiple handlers per event
        // is supported; see WorkerCreditsExhausted registered twice above).
        services.AddIntegrationEventHandler<CredentialsValidated, UsageCredentialsValidatedHandler>();
        services.AddIntegrationEventHandler<CredentialsInvalidated, UsageCredentialsInvalidatedHandler>();

        services.AddScoped<ICredentialQueries, CredentialQueries>();
        services.AddScoped<ICredentialGate, CredentialGate>();

        services.AddSharedDockerInfrastructure();
        services.AddSingleton<ICredentialsOrchestrator, CredentialsOrchestrator>();

        services.AddSingleton<ILoginSuccessCommitter, LoginSuccessCommitter>();
        services.AddSingleton<LoginSessionService>();
        services.AddSingleton<ILoginSessionState>(sp => sp.GetRequiredService<LoginSessionService>());
        services.AddHostedService<TransientContainerReaper>();

        services.AddSingleton<CreditProbeCoordinator>();
        services.AddSingleton<ICreditProbeCoordinator>(sp => sp.GetRequiredService<CreditProbeCoordinator>());
        services.AddHostedService<CreditProbeService>();

        // UsageSnapshotState singleton: both reader and mutator interfaces resolve to ONE instance.
        // Registered here (not in step 8) so the GET /api/credentials/usage endpoint resolves
        // independently of the poller. Do NOT double-register in step 8.
        services.AddSingleton<UsageSnapshotState>();
        services.AddSingleton<IUsageSnapshotState>(sp => sp.GetRequiredService<UsageSnapshotState>());
        services.AddSingleton<IUsageSnapshotStateMutator>(sp => sp.GetRequiredService<UsageSnapshotState>());

        // UsagePollCoordinator singleton — depends on IUsageSnapshotBroadcaster.
        services.AddSingleton<UsagePollCoordinator>();
        services.AddSingleton<IUsagePollCoordinator>(sp => sp.GetRequiredService<UsagePollCoordinator>());
        services.AddHostedService<UsagePollService>();

        // No-op fallback so the module is self-contained; the WebApi host's later plain
        // AddSingleton<IUsageSnapshotBroadcaster, SignalRUsageSnapshotBroadcaster> wins because
        // .NET DI resolves the last registration for GetRequiredService.
        services.TryAddSingleton<IUsageSnapshotBroadcaster, NoOpUsageSnapshotBroadcaster>();

        return services;
    }

    public static IEndpointRouteBuilder MapCredentialsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapCredentialEndpoints();
        app.MapLoginEndpoints();
        return app;
    }
}
