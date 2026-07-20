using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Contracts.Queries;
using Foundry.Modules.Credentials.Features;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Infrastructure;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Docker;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Modules.Credentials;

public static class CredentialsModule
{
    public static IServiceCollection AddCredentialsModule(this IServiceCollection services)
    {
        services.AddQueryHandler<GetCredentials.Query, ClaudeAccountSummary, GetCredentials.Handler>();
        services.AddCommandHandler<UpdateAuthMode.Command, ClaudeAccountSummary, UpdateAuthMode.Handler, UpdateAuthMode.Validator>();
        services.AddHostedService<ClaudeAccountSeeder>();

        services.AddIntegrationEventHandler<WorkerAuthenticationFailed, WorkerAuthenticationFailedHandler>();
        services.AddIntegrationEventHandler<CredentialsInvalidated, CredentialsInvalidatedBroadcastHandler>();
        services.AddIntegrationEventHandler<CredentialsValidated, CredentialsValidatedBroadcastHandler>();

        services.AddScoped<ICredentialQueries, CredentialQueries>();
        services.AddScoped<ICredentialGate, CredentialGate>();

        services.AddSharedDockerInfrastructure();
        services.AddSingleton<ICredentialsOrchestrator, CredentialsOrchestrator>();

        services.AddSingleton<ILoginSuccessCommitter, LoginSuccessCommitter>();
        services.AddSingleton<LoginSessionService>();
        services.AddSingleton<ILoginSessionState>(sp => sp.GetRequiredService<LoginSessionService>());
        services.AddHostedService<TransientContainerReaper>();

        return services;
    }

    public static IEndpointRouteBuilder MapCredentialsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapCredentialEndpoints();
        app.MapLoginEndpoints();
        return app;
    }
}
