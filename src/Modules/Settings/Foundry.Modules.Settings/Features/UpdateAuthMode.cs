using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Infrastructure;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features;

internal static class UpdateAuthMode
{
    private const string ApiKeyMode = "api_key";
    private const string OAuthMode = "oauth";

    internal sealed record Command(string Mode, string? ApiKey, string? Placeholder) : ICommand<Response>;

    internal sealed record Response(
        string AuthMode,
        int MaxConcurrent,
        int TimeoutMinutes,
        bool AccessTokenPresent,
        bool RefreshTokenPresent,
        DateTimeOffset? ExpiresAt,
        string? SubscriptionType);

    internal sealed class Validator : ICommandValidator<Command>
    {
        public Result Validate(Command command)
        {
            if (command.Mode != ApiKeyMode && command.Mode != OAuthMode)
            {
                return SettingsErrors.InvalidAuthMode;
            }

            if (command.Mode == ApiKeyMode && string.IsNullOrWhiteSpace(command.ApiKey))
            {
                return SettingsErrors.InvalidAuthMode;
            }

            return Result.Ok();
        }
    }

    internal sealed class Handler(DbContext dbContext, IOAuthCredentialScanner scanner)
        : ICommandHandler<Command, Response>
    {
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
                .FirstOrDefaultAsync(cancellationToken);

            if (settings is null)
            {
                return Result<Response>.Fail(SettingsErrors.NotFound);
            }

            AuthMode mode;

            if (command.Mode == ApiKeyMode)
            {
                mode = new AuthMode.ApiKey(command.ApiKey!);
            }
            else
            {
                Result<OAuthCredentials> scanResult = await scanner.ScanAsync(cancellationToken);
                if (scanResult is Result<OAuthCredentials>.Failure failure)
                {
                    return Result<Response>.Fail(failure.Error);
                }

                OAuthCredentials credentials = ((Result<OAuthCredentials>.Success)scanResult).Value;
                mode = new AuthMode.OAuth(
                    credentials.AccessToken,
                    credentials.RefreshToken,
                    credentials.ExpiresAt,
                    credentials.SubscriptionType);
            }

            settings.SetAuthMode(mode);
            await dbContext.SaveChangesAsync(cancellationToken);

            return MapToResponse(settings);
        }

        private static Response MapToResponse(GlobalSettings settings)
        {
            AuthMode.OAuth? oauth = settings.AuthMode as AuthMode.OAuth;

            string authModeName = settings.AuthMode switch
            {
                AuthMode.ApiKey => "ApiKey",
                AuthMode.OAuth => "OAuth",
                _ => "Unknown",
            };

            return new Response(
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
        private sealed record RequestBody(string Mode, string? ApiKey);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("/auth", static async (
                    RequestBody body,
                    ICommandHandler<Command, Response> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(body.Mode, body.ApiKey, null);
                    Result<Response> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<Response>, NotFound, BadRequest<string>>>(
                        response => TypedResults.Ok(response),
                        error => error.Code switch
                        {
                            SettingsErrors.NotFoundCode => TypedResults.NotFound(),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("UpdateAuthMode")
                .WithSummary("Updates the authentication mode used by workers")
                .Produces<Response>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
