using System.Diagnostics;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static partial class UpdateAccount
{
    internal sealed record Command(
        AccountId Id,
        string Name,
        string BaseUrl,
        string? Token) : ICommand<AccountSummary>;

    internal sealed partial class Validator : ICommandValidator<Command>
    {
        internal const string NameEmptyCode = "UpdateAccount.NameEmpty";
        internal const string BaseUrlInvalidCode = "UpdateAccount.BaseUrlInvalid";
        internal const string TokenInvalidCharsCode = "UpdateAccount.TokenInvalidChars";

        [GeneratedRegex(@"^[a-zA-Z0-9\-_.]+$")]
        private static partial Regex ValidTokenCharactersRegex();

        public Result Validate(Command command)
        {
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                return new Error(NameEmptyCode, "Account name must not be empty.");
            }

            if (!Uri.TryCreate(command.BaseUrl, UriKind.Absolute, out Uri? baseUri) ||
                baseUri.Scheme is not "https")
            {
                return new Error(BaseUrlInvalidCode, "Base URL must be a valid HTTPS URL.");
            }

            if (command.Token is not null && !ValidTokenCharactersRegex().IsMatch(command.Token))
            {
                return new Error(
                    TokenInvalidCharsCode,
                    "Token contains invalid characters. Only alphanumeric characters, hyphens, underscores, and dots are allowed.");
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
            if (await dbContext.Set<Account>()
                    .FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken)
                is not Account account)
            {
                return Result<AccountSummary>.Fail(AccountErrors.NotFound(command.Id));
            }

            bool nameConflict = await dbContext.Set<Account>()
                .AsNoTracking()
                .Where(a => a.Name == command.Name)
                .AnyAsync(a => a.Id != command.Id, cancellationToken);

            if (nameConflict)
            {
                return Result<AccountSummary>.Fail(AccountErrors.DuplicateName(command.Name));
            }

            Uri baseUrl = new(command.BaseUrl);

            if (command.Token is not null)
            {
                string providerTypeForValidation = account is GitLabAccount
                    ? ProviderTypes.GitLab
                    : ProviderTypes.GitHub;

                Uri apiBaseUrl = account is GitLabAccount
                    ? GitLabAccount.DeriveApiBaseUrl(baseUrl)
                    : GitHubAccount.DeriveApiBaseUrl(baseUrl);

                ValidateToken.Query tokenQuery = new(command.Token, apiBaseUrl, providerTypeForValidation);
                Result<ValidateToken.Response> tokenResult = await validateToken.HandleAsync(
                    tokenQuery,
                    cancellationToken);

                if (tokenResult is Result<ValidateToken.Response>.Failure tokenFailure)
                {
                    return Result<AccountSummary>.Fail(tokenFailure.Error);
                }

                if (tokenResult is Result<ValidateToken.Response>.Success tokenSuccess &&
                    !tokenSuccess.Value.IsValid)
                {
                    return Result<AccountSummary>.Fail(AccountErrors.InvalidToken);
                }
            }

            switch (account)
            {
                case GitHubAccount gitHubAccount:
                    gitHubAccount.Update(command.Name, command.Token, baseUrl);
                    break;
                case GitLabAccount gitLabAccount:
                    gitLabAccount.Update(command.Name, command.Token, baseUrl);
                    break;
                default:
                    throw new UnreachableException(
                        $"No Update handler for account type '{account.GetType().Name}'.");
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            string providerType = account is GitLabAccount ? ProviderTypes.GitLab : ProviderTypes.GitHub;
            AccountSummary summary = new(
                account.Id.Value,
                account.Name,
                providerType,
                account.BaseUrl.ToString(),
                account.Token is not null);

            return Result<AccountSummary>.Ok(summary);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(string Name, string BaseUrl, string? Token);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("{id:guid}", static async (
                    Guid id,
                    RequestBody body,
                    ICommandHandler<Command, AccountSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    AccountId accountId = AccountId.From(id);
                    Command command = new(accountId, body.Name, body.BaseUrl, body.Token);
                    Result<AccountSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<AccountSummary>, NotFound, Conflict<string>, BadRequest<string>>>(
                        account => TypedResults.Ok(account),
                        error => error.Code switch
                        {
                            AccountErrors.NotFoundCode => TypedResults.NotFound(),
                            AccountErrors.DuplicateNameCode => TypedResults.Conflict(error.Message),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("UpdateAccount")
                .WithSummary("Updates an existing account")
                .Produces<AccountSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
