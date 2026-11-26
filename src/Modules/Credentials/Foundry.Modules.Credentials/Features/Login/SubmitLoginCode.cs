using Foundry.Modules.Credentials.Domain.ValueObjects;
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
/// Returns 400 when the code is not a well-formed <see cref="LoginCode"/> (checked before the
/// session is looked at, so the operator sees the shape problem first), 422 when there is no
/// active session or the session is not in a state that accepts a code, 200 on acceptance.
/// </summary>
internal static class SubmitLoginCode
{
    internal sealed record Request(string Code);

    internal static class Endpoint
    {
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
            Result<LoginCode> codeResult = LoginCode.Create(request.Code ?? string.Empty);
            if (codeResult is Result<LoginCode>.Failure codeFailure)
            {
                return codeFailure.Error.ToProblem(StatusCodes.Status400BadRequest);
            }

            LoginCode code = ((Result<LoginCode>.Success)codeResult).Value;
            Result submitResult = await service.SubmitCodeAsync(code, cancellationToken);

            return submitResult.Match<Results<Ok, ProblemHttpResult>>(
                () => TypedResults.Ok(),
                error => error.ToProblem(StatusCodes.Status422UnprocessableEntity));
        }
    }
}
