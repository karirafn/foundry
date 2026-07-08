using System.Diagnostics;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using BaseUrlVo = Foundry.Modules.Monitoring.Domain.ValueObjects.BaseUrl;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static partial class CreateAccount
{
    internal sealed record Command(
        string ProviderType,
        string BaseUrl,
        string Token) : ICommand<AccountSummary>;

    internal sealed partial class Validator : ICommandValidator<Command>
    {
        internal const string InvalidProviderTypeCode = "CreateAccount.InvalidProviderType";
        internal const string TokenEmptyCode = "CreateAccount.TokenEmpty";
        internal const string TokenInvalidCharsCode = "CreateAccount.TokenInvalidChars";

        [GeneratedRegex(@"^[a-zA-Z0-9\-_.]+$")]
        private static partial Regex ValidTokenCharactersRegex();

        public Result Validate(Command command)
        {
            Result<BaseUrlVo> baseUrlResult = BaseUrlVo.Create(command.BaseUrl);
            if (baseUrlResult is Result<BaseUrlVo>.Failure baseUrlFailure)
            {
                return baseUrlFailure.Error;
            }

            if (string.IsNullOrWhiteSpace(command.Token))
            {
                return new Error(TokenEmptyCode, "Token must not be empty.");
            }

            if (!ValidTokenCharactersRegex().IsMatch(command.Token))
            {
                return new Error(
                    TokenInvalidCharsCode,
                    "Token contains invalid characters. Only alphanumeric characters, hyphens, underscores, and dots are allowed.");
            }

            bool isKnownProvider = ProviderTypes.IsKnown(command.ProviderType);

            if (!isKnownProvider)
            {
                return new Error(
                    InvalidProviderTypeCode,
                    $"Provider type '{command.ProviderType}' is not supported. Only 'github' and 'gitlab' are supported.");
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
            if (BaseUrlVo.Create(command.BaseUrl) is not Result<BaseUrlVo>.Success { Value: BaseUrlVo baseUrl })
            {
                throw new UnreachableException("BaseUrl validated in the validator but failed in the handler.");
            }

            bool isGitLab = string.Equals(command.ProviderType, ProviderTypes.GitLab, StringComparison.OrdinalIgnoreCase);

            Uri apiBaseUrl = isGitLab
                ? GitLabAccount.DeriveApiBaseUrl(baseUrl)
                : GitHubAccount.DeriveApiBaseUrl(baseUrl);

            ValidateToken.Query tokenQuery = new(command.Token, apiBaseUrl, command.ProviderType);
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

            if (string.IsNullOrWhiteSpace(tokenResponse.AccountName))
            {
                return Result<AccountSummary>.Fail(AccountErrors.UnresolvedIdentity);
            }

            string accountName = tokenResponse.AccountName;

            Account account = isGitLab
                ? GitLabAccount.Create(accountName, command.Token, baseUrl)
                : GitHubAccount.Create(accountName, command.Token, baseUrl);

            dbContext.Set<Account>().Add(account);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                return Result<AccountSummary>.Fail(AccountErrors.DuplicateName(accountName));
            }

            string providerType = isGitLab ? ProviderTypes.GitLab : ProviderTypes.GitHub;
            AccountSummary summary = new(
                account.Id.Value,
                account.Name,
                providerType,
                account.BaseUrl.Value.ToString(),
                account.Token is not null);

            return Result<AccountSummary>.Ok(summary);
        }
    }

    // SQLite error code 19 is SQLITE_CONSTRAINT (unique constraint violation).
    // The monitoring module does not reference the SQLite provider directly,
    // so we detect the violation via the exception message instead of SqliteException.SqliteErrorCode.
    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;

    internal static class Endpoint
    {
        private sealed record RequestBody(
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
                    Command command = new(body.ProviderType, body.BaseUrl, body.Token);
                    Result<AccountSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Created<AccountSummary>, Conflict<string>, BadRequest<string>>>(
                        account => TypedResults.Created($"/api/accounts/{account.Id}", account),
                        error => error.Code switch
                        {
                            AccountErrors.DuplicateNameCode => TypedResults.Conflict(error.Message),
                            AccountErrors.UnresolvedIdentityCode => TypedResults.BadRequest(error.Message),
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
