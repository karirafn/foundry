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

internal static partial class UpdateAccount
{
    internal sealed record Command(
        CredentialId Id,
        string BaseUrl,
        string? Token) : ICommand<CredentialSummary>;

    internal sealed partial class Validator : ICommandValidator<Command>
    {
        internal const string TokenInvalidCharsCode = "UpdateAccount.TokenInvalidChars";

        [GeneratedRegex(@"^[a-zA-Z0-9\-_.]+$")]
        private static partial Regex ValidTokenCharactersRegex();

        public Result Validate(Command command)
        {
            Result<BaseUrlVo> baseUrlResult = BaseUrlVo.Create(command.BaseUrl);
            if (baseUrlResult is Result<BaseUrlVo>.Failure baseUrlFailure)
            {
                return baseUrlFailure.Error;
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
        : ICommandHandler<Command, CredentialSummary>
    {
        public async Task<Result<CredentialSummary>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            if (await dbContext.Set<Credential>()
                    .FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken)
                is not Credential credential)
            {
                return Result<CredentialSummary>.Fail(CredentialErrors.NotFound(command.Id));
            }

            if (BaseUrlVo.Create(command.BaseUrl) is not Result<BaseUrlVo>.Success { Value: BaseUrlVo baseUrl })
            {
                throw new UnreachableException("BaseUrl validated in the validator but failed in the handler.");
            }

            string accountName = credential.Name;

            if (command.Token is not null)
            {
                string providerTypeForValidation = credential is GitLabCredential
                    ? ProviderTypes.GitLab
                    : ProviderTypes.GitHub;

                Uri apiBaseUrl = credential is GitLabCredential
                    ? GitLabCredential.DeriveApiBaseUrl(baseUrl)
                    : GitHubCredential.DeriveApiBaseUrl(baseUrl);

                ValidateToken.Query tokenQuery = new(command.Token, apiBaseUrl, providerTypeForValidation);
                Result<ValidateToken.Response> tokenResult = await validateToken.HandleAsync(
                    tokenQuery,
                    cancellationToken);

                if (tokenResult is Result<ValidateToken.Response>.Failure tokenFailure)
                {
                    return Result<CredentialSummary>.Fail(tokenFailure.Error);
                }

                // tokenResult is guaranteed Success here — Failure was handled above.
                ValidateToken.Response tokenResponse = ((Result<ValidateToken.Response>.Success)tokenResult).Value;

                if (!tokenResponse.IsValid)
                {
                    return Result<CredentialSummary>.Fail(CredentialErrors.InvalidToken);
                }

                if (string.IsNullOrWhiteSpace(tokenResponse.AccountName))
                {
                    return Result<CredentialSummary>.Fail(CredentialErrors.UnresolvedIdentity);
                }

                string resolvedName = tokenResponse.AccountName;

                if (resolvedName.Length > AccountsDatabaseHelpers.AccountNameMaxLength || resolvedName.Any(char.IsControl))
                {
                    return Result<CredentialSummary>.Fail(CredentialErrors.UnresolvedIdentity);
                }

                accountName = resolvedName;
            }

            switch (credential)
            {
                case GitHubCredential gitHubCredential:
                    gitHubCredential.Update(accountName, command.Token, baseUrl);
                    break;
                case GitLabCredential gitLabCredential:
                    gitLabCredential.Update(accountName, command.Token, baseUrl);
                    break;
                default:
                    throw new UnreachableException(
                        $"No Update handler for credential type '{credential.GetType().Name}'.");
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (AccountsDatabaseHelpers.IsAccountNameDuplicateViolation(ex))
            {
                return Result<CredentialSummary>.Fail(CredentialErrors.DuplicateName(accountName));
            }

            string providerType = credential is GitLabCredential ? ProviderTypes.GitLab : ProviderTypes.GitHub;
            CredentialSummary summary = new(
                credential.Id.Value,
                credential.Name,
                providerType,
                credential.BaseUrl.Value.ToString(),
                credential.Token is not null);

            return Result<CredentialSummary>.Ok(summary);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(string BaseUrl, string? Token);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("{id:guid}", static async (
                    Guid id,
                    RequestBody body,
                    ICommandHandler<Command, CredentialSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    CredentialId credentialId = CredentialId.From(id);
                    Command command = new(credentialId, body.BaseUrl, body.Token);
                    Result<CredentialSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<CredentialSummary>, NotFound, Conflict<string>, BadRequest<string>>>(
                        credential => TypedResults.Ok(credential),
                        error => error.Code switch
                        {
                            CredentialErrors.NotFoundCode => TypedResults.NotFound(),
                            CredentialErrors.DuplicateNameCode => TypedResults.Conflict(error.Message),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("UpdateAccount")
                .WithSummary("Updates an existing account")
                .Produces<CredentialSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
