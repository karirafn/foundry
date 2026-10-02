using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Credentials.Features.Login;

/// <summary>
/// POST /api/credentials/login/code
/// Submits the operator's authorization code to the active login session.
/// Returns 400 when the code is empty/whitespace or exceeds the maximum length,
/// 422 when there is no active session or the session is not in a state that
/// accepts a code, 200 on acceptance.
/// </summary>
internal static class SubmitLoginCode
{
    internal sealed record Request(string Code);

    internal static class Endpoint
    {
        private const int MaxLoginCodeLength = 512;

        internal static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/code", static async (
                    Request request,
                    LoginSessionService service,
                    CancellationToken cancellationToken) =>
                await HandleAsync(request, service, cancellationToken))
                .WithName("SubmitOAuthLoginCode")
                .WithSummary("Submits the authorization code to the active login session")
                .Produces(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        }

        internal static async Task<Results<Ok, ProblemHttpResult>> HandleAsync(
            Request request,
            LoginSessionService service,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return CredentialsErrors.LoginCodeEmpty.ToProblem(StatusCodes.Status400BadRequest);
            }

            if (request.Code.Length > MaxLoginCodeLength)
            {
                return CredentialsErrors.LoginCodeTooLong(MaxLoginCodeLength)
                    .ToProblem(StatusCodes.Status400BadRequest);
            }

            Result submitResult = await service.SubmitCodeAsync(request.Code, cancellationToken);

            return submitResult.Match<Results<Ok, ProblemHttpResult>>(
                () => TypedResults.Ok(),
                error => error.ToProblem(StatusCodes.Status422UnprocessableEntity));
        }
    }
}
