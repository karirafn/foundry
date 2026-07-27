using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Credentials.Features;

internal static class UpdateAuthMode
{
    private const string ApiKeyMode = "api_key";
    private const string OAuthMode = "oauth";

    internal sealed record Command(string Mode, string? ApiKey) : ICommand<ClaudeAccountSummary>;

    internal sealed class Validator : ICommandValidator<Command>
    {
        private const int MaxApiKeyLength = 256;

        public Result Validate(Command command)
        {
            if (command.Mode != ApiKeyMode && command.Mode != OAuthMode)
            {
                return CredentialsErrors.InvalidAuthMode;
            }

            if (command.Mode == ApiKeyMode && string.IsNullOrWhiteSpace(command.ApiKey))
            {
                return CredentialsErrors.InvalidAuthMode;
            }

            if (command.Mode == ApiKeyMode && command.ApiKey?.Length > MaxApiKeyLength)
            {
                return CredentialsErrors.InvalidAuthMode;
            }

            return Result.Ok();
        }
    }

    internal sealed class Handler(DbContext dbContext) : ICommandHandler<Command, ClaudeAccountSummary>
    {
        public async Task<Result<ClaudeAccountSummary>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
                .FirstOrDefaultAsync(cancellationToken);

            if (account is null)
            {
                return Result<ClaudeAccountSummary>.Fail(CredentialsErrors.NotFound);
            }

            AuthMode mode = command.Mode == ApiKeyMode
                ? new AuthMode.ApiKey(command.ApiKey!)
                : new AuthMode.OAuth(SubscriptionType: null);

            account.SetAuthMode(mode);
            await dbContext.SaveChangesAsync(cancellationToken);

            return CredentialQueries.ToSummary(account);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(string Mode, string? ApiKey);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("/auth", static async (
                    RequestBody body,
                    ICommandHandler<Command, ClaudeAccountSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(body.Mode, body.ApiKey);
                    Result<ClaudeAccountSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<ClaudeAccountSummary>, NotFound, BadRequest<string>>>(
                        summary => TypedResults.Ok(summary),
                        error => error.Code switch
                        {
                            CredentialsErrors.NotFoundCode => TypedResults.NotFound(),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("UpdateCredentialsAuthMode")
                .WithSummary("Updates the authentication mode for Claude account credentials")
                .Produces<ClaudeAccountSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
