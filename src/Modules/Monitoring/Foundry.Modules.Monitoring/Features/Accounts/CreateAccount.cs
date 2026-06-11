using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class CreateAccount
{
    private const string GitHubProviderType = "github";

    internal sealed record Command(
        string Name,
        string ProviderType,
        string BaseUrl,
        string Token) : ICommand<AccountSummary>;

    internal sealed class Validator : ICommandValidator<Command>
    {
        internal const string InvalidProviderTypeCode = "CreateAccount.InvalidProviderType";
        internal const string NameEmptyCode = "CreateAccount.NameEmpty";
        internal const string BaseUrlInvalidCode = "CreateAccount.BaseUrlInvalid";
        internal const string TokenEmptyCode = "CreateAccount.TokenEmpty";

        public Result Validate(Command command)
        {
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                return new Error(NameEmptyCode, "Account name must not be empty.");
            }

            if (!Uri.TryCreate(command.BaseUrl, UriKind.Absolute, out Uri? baseUri) ||
                baseUri.Scheme != Uri.UriSchemeHttps)
            {
                return new Error(BaseUrlInvalidCode, "Base URL must be a valid HTTPS URL.");
            }

            if (string.IsNullOrWhiteSpace(command.Token))
            {
                return new Error(TokenEmptyCode, "Token must not be empty.");
            }

            if (!string.Equals(command.ProviderType, GitHubProviderType, StringComparison.OrdinalIgnoreCase))
            {
                return new Error(InvalidProviderTypeCode, $"Provider type '{command.ProviderType}' is not supported. Only 'github' is supported.");
            }

            return Result.Ok();
        }
    }

    internal sealed class Handler(
        DbContext dbContext,
        IQueryHandler<ValidateToken.Query, ValidateToken.Response> validateToken)
        : ICommandHandler<Command, AccountSummary>
    {
        public async Task<Result<AccountSummary>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            bool nameExists = await dbContext.Set<Account>()
                .AsNoTracking()
                .AnyAsync(a => a.Name == command.Name, cancellationToken);

            if (nameExists)
            {
                return Result<AccountSummary>.Fail(AccountErrors.DuplicateName(command.Name));
            }

            Uri baseUrl = new(command.BaseUrl);
            GitHubAccount temp = GitHubAccount.Create(command.Name, command.Token, baseUrl);

            ValidateToken.Query tokenQuery = new(command.Token, temp.ApiBaseUrl);
            Result<ValidateToken.Response> tokenResult = await validateToken.HandleAsync(
                tokenQuery,
                cancellationToken);

            if (tokenResult is not Result<ValidateToken.Response>.Success tokenSuccess)
            {
                Error error = ((Result<ValidateToken.Response>.Failure)tokenResult).Error;
                return Result<AccountSummary>.Fail(error);
            }

            ValidateToken.Response tokenResponse = tokenSuccess.Value;
            if (!tokenResponse.IsValid)
            {
                return Result<AccountSummary>.Fail(AccountErrors.InvalidToken);
            }

            GitHubAccount account = GitHubAccount.Create(command.Name, command.Token, baseUrl);
            dbContext.Set<Account>().Add(account);
            await dbContext.SaveChangesAsync(cancellationToken);

            AccountSummary summary = new(
                account.Id.Value,
                account.Name,
                GitHubProviderType,
                account.BaseUrl.ToString(),
                account.Token is not null);

            return Result<AccountSummary>.Ok(summary);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(
            string Name,
            string ProviderType,
            string BaseUrl,
            string Token);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost(string.Empty, static async (
                    RequestBody body,
                    ICommandHandler<Command, AccountSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(body.Name, body.ProviderType, body.BaseUrl, body.Token);
                    Result<AccountSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Created<AccountSummary>, Conflict<string>, BadRequest<string>>>(
                        account => TypedResults.Created($"/api/accounts/{account.Id}", account),
                        error => error.Code switch
                        {
                            AccountErrors.DuplicateNameCode => TypedResults.Conflict(error.Message),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("CreateAccount")
                .WithSummary("Creates a new account")
                .Produces<AccountSummary>(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status409Conflict);
        }
    }
}
