using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features;

internal static class GetSettings
{
    internal sealed record Query : IQuery<GlobalSettingsSummary>;

    internal sealed class Handler(DbContext dbContext) : IQueryHandler<Query, GlobalSettingsSummary>
    {
        public async Task<Result<GlobalSettingsSummary>> HandleAsync(
            Query query,
            CancellationToken cancellationToken)
        {
            GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (settings is null)
            {
                return Result<GlobalSettingsSummary>.Fail(SettingsErrors.NotFound);
            }

            return MapToSummary(settings);
        }

        private static GlobalSettingsSummary MapToSummary(GlobalSettings settings)
        {
            AuthMode.OAuth? oauth = settings.AuthMode as AuthMode.OAuth;

            string authModeName = settings.AuthMode switch
            {
                AuthMode.ApiKey => "ApiKey",
                AuthMode.OAuth => "OAuth",
                _ => "Unknown",
            };

            return new GlobalSettingsSummary(
                authModeName,
                settings.MaxConcurrent,
                settings.TimeoutMinutes,
                oauth is not null && oauth.AccessToken.Length > 0,
                oauth is not null && oauth.RefreshToken.Length > 0,
                oauth?.ExpiresAt,
                oauth?.SubscriptionType);
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/", static async (
                    IQueryHandler<Query, GlobalSettingsSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<GlobalSettingsSummary> result = await handler.HandleAsync(
                        new Query(),
                        cancellationToken);

                    return result.Match<Results<Ok<GlobalSettingsSummary>, NotFound>>(
                        summary => TypedResults.Ok(summary),
                        _ => TypedResults.NotFound());
                })
                .WithName("GetSettings")
                .WithSummary("Gets the current global settings")
                .Produces<GlobalSettingsSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }
}
