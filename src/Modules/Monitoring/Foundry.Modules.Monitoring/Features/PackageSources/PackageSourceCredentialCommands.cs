using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// Registers or removes a source's Package Source Credential. Either change is committed, then the source is probed
/// again at once outside the request (<see cref="PackageSourceProbeRequests"/>).
/// </summary>
internal static class PackageSourceCredentialCommands
{
    internal const string NotFoundCode = "PackageSource.NotFound";

    internal static Error NotFound(PackageSourceId id) =>
        new(NotFoundCode, $"Package source '{id.Value}' was not found.") { Kind = ErrorKind.NotFound };

    internal sealed class Handler(
        DbContext dbContext,
        PackageSourceProbeRequests probeRequests,
        TimeProvider timeProvider)
    {
        /// <summary>
        /// Registers <paramref name="username"/> and <paramref name="token"/> for the source. Both must already meet
        /// <see cref="PackageSourceCredential.Validate"/>.
        /// </summary>
        public Task<Result> SetAsync(PackageSourceId id, string username, string token, CancellationToken cancellationToken) =>
            ChangeAsync(id, source => source.SetCredential(username, token, timeProvider.GetUtcNow()), cancellationToken);

        public Task<Result> RemoveAsync(PackageSourceId id, CancellationToken cancellationToken) =>
            ChangeAsync(id, source => source.RemoveCredential(), cancellationToken);

        private async Task<Result> ChangeAsync(
            PackageSourceId id,
            Action<PackageSource> change,
            CancellationToken cancellationToken)
        {
            PackageSource? source = await dbContext.Set<PackageSource>()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (source is null)
            {
                return NotFound(id);
            }

            change(source);
            await dbContext.SaveChangesAsync(cancellationToken);

            probeRequests.Request(id);
            return Result.Ok();
        }
    }

    internal static class Endpoint
    {
        internal const string UsernameField = "username";
        internal const string TokenField = "token";

        /// <summary>The credential to register. Both are write-only; the token is never returned.</summary>
        private sealed record RequestBody(string? Username, string? Token);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("{id:guid}/credential", static async (
                    Guid id,
                    RequestBody body,
                    Handler handler,
                    CancellationToken cancellationToken) =>
                {
                    IReadOnlyList<PackageSourceCredentialFieldError> errors =
                        PackageSourceCredential.Validate(body.Username, body.Token);
                    if (errors.Count > 0)
                    {
                        return (Results<NoContent, ProblemHttpResult>)TypedResults.Problem(new HttpValidationProblemDetails(
                            errors
                                .GroupBy(e => FieldName(e.Field))
                                .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()))
                        {
                            Status = StatusCodes.Status400BadRequest,
                        });
                    }

                    Result result = await handler.SetAsync(
                        PackageSourceId.From(id),
                        body.Username!,
                        body.Token!,
                        cancellationToken);

                    return result.Match<Results<NoContent, ProblemHttpResult>>(
                        () => TypedResults.NoContent(),
                        error => error.ToProblem(StatusCodes.Status404NotFound));
                })
                .WithName("SetPackageSourceCredential")
                .WithSummary("Registers the credential Foundry reads a package source with")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status404NotFound);

            group.MapDelete("{id:guid}/credential", static async (
                    Guid id,
                    Handler handler,
                    CancellationToken cancellationToken) =>
                {
                    Result result = await handler.RemoveAsync(PackageSourceId.From(id), cancellationToken);

                    return result.Match<Results<NoContent, ProblemHttpResult>>(
                        () => TypedResults.NoContent(),
                        error => error.ToProblem(StatusCodes.Status404NotFound));
                })
                .WithName("RemovePackageSourceCredential")
                .WithSummary("Removes a package source's credential, so its repositories fall back to their own")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status404NotFound);
        }

        private static string FieldName(PackageSourceCredentialField field) => field switch
        {
            PackageSourceCredentialField.Username => UsernameField,
            PackageSourceCredentialField.Token => TokenField,
            _ => throw new System.Diagnostics.UnreachableException($"Unhandled credential field: {field}"),
        };
    }
}

internal static class PackageSourceEndpoints
{
    internal static IEndpointRouteBuilder MapPackageSourceEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/package-sources")
            .WithTags("PackageSources");

        GetPackageSources.Endpoint.Map(group);
        PackageSourceCredentialCommands.Endpoint.Map(group);

        return routes;
    }
}
