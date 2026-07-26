using System.Diagnostics;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
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
        string Token) : ICommand<CredentialSummary>;

    internal sealed partial class Validator : ICommandValidator<Command>
    {
        internal const string InvalidProviderTypeCode = "CreateAccount.InvalidProviderType";
        internal const string TokenEmptyCode = "CreateAccount.TokenEmpty";
        internal const string TokenInvalidCharsCode = "CreateAccount.TokenInvalidChars";
        internal const string TokenTooLongCode = "CreateAccount.TokenTooLong";

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

            if (command.Token.Length > AccountsDatabaseHelpers.TokenMaxLength)
            {
                return new Error(
                    TokenTooLongCode,
                    $"Token must not exceed {AccountsDatabaseHelpers.TokenMaxLength} characters.");
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
        IQueryHandler<ValidateToken.Query, ValidateToken.Response> validateToken,
        INamespaceDeriver namespaceDeriver)
        : ICommandHandler<Command, CredentialSummary>
    {
        public async Task<Result<CredentialSummary>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            if (BaseUrlVo.Create(command.BaseUrl) is not Result<BaseUrlVo>.Success { Value: BaseUrlVo baseUrl })
            {
                throw new UnreachableException("BaseUrl validated in the validator but failed in the handler.");
            }

            bool isGitLab = string.Equals(command.ProviderType, ProviderTypes.GitLab, StringComparison.OrdinalIgnoreCase);

            Uri apiBaseUrl = isGitLab
                ? GitLabCredential.DeriveApiBaseUrl(baseUrl)
                : GitHubCredential.DeriveApiBaseUrl(baseUrl);

            ValidateToken.Query tokenQuery = new(command.Token, apiBaseUrl, command.ProviderType);
            Result<ValidateToken.Response> tokenResult = await validateToken.HandleAsync(
                tokenQuery,
                cancellationToken);

            if (tokenResult is not Result<ValidateToken.Response>.Success tokenSuccess)
            {
                Error error = ((Result<ValidateToken.Response>.Failure)tokenResult).Error;
                return Result<CredentialSummary>.Fail(error);
            }

            ValidateToken.Response tokenResponse = tokenSuccess.Value;
            if (!tokenResponse.IsValid)
            {
                return Result<CredentialSummary>.Fail(CredentialErrors.InvalidToken);
            }

            if (string.IsNullOrWhiteSpace(tokenResponse.AccountName))
            {
                return Result<CredentialSummary>.Fail(CredentialErrors.UnresolvedIdentity);
            }

            string accountName = tokenResponse.AccountName;

            if (accountName.Length > AccountsDatabaseHelpers.AccountNameMaxLength || accountName.Any(char.IsControl))
            {
                return Result<CredentialSummary>.Fail(CredentialErrors.UnresolvedIdentity);
            }

            Credential credential = isGitLab
                ? GitLabCredential.Create(accountName, command.Token, baseUrl)
                : GitHubCredential.Create(accountName, command.Token, baseUrl);

            NamespaceDerivationOutcome outcome = await namespaceDeriver.DeriveAsync(credential, cancellationToken);
            IReadOnlyCollection<Namespace> namespaces = outcome is NamespaceDerivationOutcome.Derived derived
                ? derived.Namespaces
                : [];

            credential.SetNamespaces(namespaces);
            dbContext.Set<Credential>().Add(credential);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (AccountsDatabaseHelpers.IsNamespaceDuplicateViolation(ex))
            {
                return Result<CredentialSummary>.Fail(CredentialErrors.DuplicateNamespace(credential.Host));
            }

            string providerType = isGitLab ? ProviderTypes.GitLab : ProviderTypes.GitHub;
            CredentialSummary summary = new(
                credential.Id.Value,
                credential.Name,
                providerType,
                credential.BaseUrl.Value.ToString(),
                credential.Token is not null,
                credential.Namespaces.Select(n => n.Value).ToList());

            return Result<CredentialSummary>.Ok(summary);
        }
    }

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
                    ICommandHandler<Command, CredentialSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(body.ProviderType, body.BaseUrl, body.Token);
                    Result<CredentialSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Created<CredentialSummary>, Conflict<string>, BadRequest<string>>>(
                        credential => TypedResults.Created($"/api/accounts/{credential.Id}", credential),
                        error => error.Code switch
                        {
                            CredentialErrors.DuplicateNamespaceCode => TypedResults.Conflict(error.Message),
                            CredentialErrors.UnresolvedIdentityCode => TypedResults.BadRequest(error.Message),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("CreateAccount")
                .WithSummary("Creates a new account")
                .Produces<CredentialSummary>(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status409Conflict);
        }
    }
}
