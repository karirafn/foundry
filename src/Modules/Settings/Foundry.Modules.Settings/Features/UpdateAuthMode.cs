using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
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

    internal sealed record Command(string Mode, string? ApiKey) : ICommand<Response>;

    internal sealed record Response(string AuthMode, int MaxConcurrent, int TimeoutMinutes);

    internal sealed class Validator : ICommandValidator<Command>
    {
        private const int MaxApiKeyLength = 256;

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

            if (command.Mode == ApiKeyMode && command.ApiKey?.Length > MaxApiKeyLength)
            {
                return SettingsErrors.InvalidAuthMode;
            }

            return Result.Ok();
        }
    }

    internal sealed class Handler(DbContext dbContext) : ICommandHandler<Command, Response>
    {
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
                .FirstOrDefaultAsync(cancellationToken);

            if (settings is null)
            {
                return Result<Response>.Fail(SettingsErrors.NotFound);
            }

            AuthMode mode = command.Mode == ApiKeyMode
                ? new AuthMode.ApiKey(command.ApiKey!)
                : new AuthMode.OAuth(SubscriptionType: null);

            settings.SetAuthMode(mode);
            await dbContext.SaveChangesAsync(cancellationToken);

            GlobalSettingsSummary summary = GlobalSettingsMapper.ToSummary(settings);
            return new Response(summary.AuthMode, summary.MaxConcurrent, summary.TimeoutMinutes);
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
                    Command command = new(body.Mode, body.ApiKey);
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
