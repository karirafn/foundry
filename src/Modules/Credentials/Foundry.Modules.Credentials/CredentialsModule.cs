using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Contracts.Queries;
using Foundry.Modules.Credentials.Features;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Modules.Credentials;

public static class CredentialsModule
{
    public static IServiceCollection AddCredentialsModule(this IServiceCollection services)
    {
        services.AddQueryHandler<GetCredentials.Query, ClaudeAccountSummary, GetCredentials.Handler>();
        services.AddHostedService<ClaudeAccountSeeder>();

        services.AddScoped<ICredentialQueries, CredentialQueries>();
        services.AddScoped<ICredentialGate, CredentialGate>();
        services.AddSingleton<ILoginSessionState, NullLoginSessionState>();

        return services;
    }

    public static IEndpointRouteBuilder MapCredentialsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapCredentialEndpoints();
        return app;
    }
}
