using System.Diagnostics;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
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
        IQueryHandler<ValidateToken.Query, ValidateToken.Response> validateToken,
        GitHubHttpClient gitHubHttpClient,
        GitLabHttpClient gitLabHttpClient)
        : ICommandHandler<Command, CredentialSummary>
    {
        public async Task<Result<CredentialSummary>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            if (await dbContext.Set<Credential>()
                    .Include(c => c.Namespaces)
                    .FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken)
                is not Credential credential)
            {
                return Result<CredentialSummary>.Fail(CredentialErrors.NotFound(command.Id));
            }

            if (BaseUrlVo.Create(command.BaseUrl) is not Result<BaseUrlVo>.Success { Value: BaseUrlVo baseUrl })
            {
                throw new UnreachableException("BaseUrl validated in the validator but failed in the handler.");
            }

            bool isGitLab = credential is GitLabCredential;
            string accountName = credential.Name;
            Uri apiBaseUrl = isGitLab
                ? GitLabCredential.DeriveApiBaseUrl(baseUrl)
                : GitHubCredential.DeriveApiBaseUrl(baseUrl);

            if (command.Token is not null)
            {
                string providerTypeForValidation = isGitLab ? ProviderTypes.GitLab : ProviderTypes.GitHub;

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

                // Re-derive namespace set from the new token's writable-repo listing.
                // The set may shrink if the new token has narrower scope.
                string tokenForListing = command.Token;
                IReadOnlyCollection<Namespace> namespaces = await DeriveNamespacesAsync(
                    isGitLab, apiBaseUrl, tokenForListing, cancellationToken);
                credential.SetNamespaces(namespaces);
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
                credential.Token is not null);

            return Result<CredentialSummary>.Ok(summary);
        }

        private async Task<IReadOnlyCollection<Namespace>> DeriveNamespacesAsync(
            bool isGitLab,
            Uri apiBaseUrl,
            string token,
            CancellationToken cancellationToken)
        {
            try
            {
                Result<IReadOnlyList<AvailableRepository>> listResult = isGitLab
                    ? await gitLabHttpClient.ListRepositoriesAsync(apiBaseUrl, token, cancellationToken)
                    : await gitHubHttpClient.ListRepositoriesAsync(apiBaseUrl, token, cancellationToken);

                if (listResult is not Result<IReadOnlyList<AvailableRepository>>.Success listSuccess)
                {
                    return [];
                }

                return NamespaceDerivation.FromWritableRepositories(listSuccess.Value);
            }
#pragma warning disable CA1031 // Provider listing may fail with any exception — treat all as empty; namespaces are best-effort at rotate time
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                return [];
            }
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
                            CredentialErrors.DuplicateNamespaceCode => TypedResults.Conflict(error.Message),
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
