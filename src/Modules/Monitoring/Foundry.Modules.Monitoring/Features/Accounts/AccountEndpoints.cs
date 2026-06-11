using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class AccountEndpoints
{
    internal static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/accounts")
            .WithTags("Accounts");

        GetAccounts.Endpoint.Map(group);
        CreateAccount.Endpoint.Map(group);
        UpdateAccount.Endpoint.Map(group);
        DeleteAccount.Endpoint.Map(group);
        ValidateToken.Endpoint.Map(group);

        return routes;
    }
}
