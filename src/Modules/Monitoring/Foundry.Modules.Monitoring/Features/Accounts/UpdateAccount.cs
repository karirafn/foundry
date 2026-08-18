using System.Diagnostics;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts.Rotation;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;
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
        string? Token) : ICommand<CredentialUpdateResult>;

    internal sealed partial class Validator : ICommandValidator<Command>
    {
        internal const string TokenInvalidCharsCode = "UpdateAccount.TokenInvalidChars";
        internal const string TokenTooLongCode = "UpdateAccount.TokenTooLong";

        [GeneratedRegex(@"^[a-zA-Z0-9\-_.]+$")]
        private static partial Regex ValidTokenCharactersRegex();

        public Result Validate(Command command)
        {
            Result<BaseUrlVo> baseUrlResult = BaseUrlVo.Create(command.BaseUrl);
            if (baseUrlResult is Result<BaseUrlVo>.Failure baseUrlFailure)
            {
                return baseUrlFailure.Error;
            }

            if (command.Token is not null && command.Token.Length > AccountsDatabaseHelpers.TokenMaxLength)
            {
                return new Error(
                    TokenTooLongCode,
                    $"Token must not exceed {AccountsDatabaseHelpers.TokenMaxLength} characters.");
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
        INamespaceDeriver namespaceDeriver,
        CredentialRotationService rotationService)
        : ICommandHandler<Command, CredentialUpdateResult>
    {
        public async Task<Result<CredentialUpdateResult>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            if (await dbContext.Set<Credential>()
                    .Include(c => c.Namespaces)
                    .FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken)
                is not Credential credential)
            {
                return Result<CredentialUpdateResult>.Fail(CredentialErrors.NotFound(command.Id));
            }

            if (BaseUrlVo.Create(command.BaseUrl) is not Result<BaseUrlVo>.Success { Value: BaseUrlVo baseUrl })
            {
                throw new UnreachableException("BaseUrl validated in the validator but failed in the handler.");
            }

            bool isGitLab = credential is GitLabCredential;
            string accountName = credential.Name;

            IReadOnlyList<AffectedRepository> affectedRepositories = [];

            if (command.Token is not null)
            {
                Uri apiBaseUrl = isGitLab
                    ? GitLabCredential.DeriveApiBaseUrl(baseUrl)
                    : GitHubCredential.DeriveApiBaseUrl(baseUrl);

                string providerTypeForValidation = isGitLab ? ProviderTypes.GitLab : ProviderTypes.GitHub;

                ValidateToken.Query tokenQuery = new(command.Token, apiBaseUrl, providerTypeForValidation);
                Result<ValidateToken.Response> tokenResult = await validateToken.HandleAsync(
                    tokenQuery,
                    cancellationToken);

                if (tokenResult is Result<ValidateToken.Response>.Failure tokenFailure)
                {
                    return Result<CredentialUpdateResult>.Fail(tokenFailure.Error);
                }

                if (tokenResult is not Result<ValidateToken.Response>.Success { Value: ValidateToken.Response tokenResponse })
                {
                    throw new UnreachableException(
                        $"Token validation returned an unexpected result type: {tokenResult.GetType().Name}");
                }

                string? resolvedName = tokenResponse.Kind switch
                {
                    ValidateToken.Kinds.Authenticated when tokenResponse.MissingScopes.Count == 0
                        => tokenResponse.AccountName,
                    ValidateToken.Kinds.ScopesUnverifiable
                        => tokenResponse.AccountName,
                    _ => null,
                };

                if (resolvedName is null)
                {
                    Error kindError = tokenResponse.Kind switch
                    {
                        ValidateToken.Kinds.Authenticated => CredentialErrors.InvalidToken,
                        ValidateToken.Kinds.AuthenticationFailed => CredentialErrors.InvalidToken,
                        ValidateToken.Kinds.IdentityUnresolved => CredentialErrors.UnresolvedIdentity,
                        ValidateToken.Kinds.ScopesUnverifiable => CredentialErrors.UnresolvedIdentity,
                        ValidateToken.Kinds.ProviderMismatch =>
                            CredentialErrors.ProviderMismatch(tokenResponse.DetectedProvider ?? string.Empty),
                        _ => CredentialErrors.InvalidToken,
                    };
                    return Result<CredentialUpdateResult>.Fail(kindError);
                }

                if (string.IsNullOrWhiteSpace(resolvedName))
                {
                    return Result<CredentialUpdateResult>.Fail(CredentialErrors.UnresolvedIdentity);
                }

                if (resolvedName.Length > AccountsDatabaseHelpers.AccountNameMaxLength || resolvedName.Any(char.IsControl))
                {
                    return Result<CredentialUpdateResult>.Fail(CredentialErrors.UnresolvedIdentity);
                }

                accountName = resolvedName;

                Result<IReadOnlyCollection<Namespace>> deriveResult = await DeriveAndGuardAsync(
                    accountName,
                    credential.Id.Value,
                    apiBaseUrl,
                    command.Token!,
                    baseUrl,
                    isGitLab,
                    cancellationToken);

                if (deriveResult is Result<IReadOnlyCollection<Namespace>>.Failure deriveFailure)
                {
                    return Result<CredentialUpdateResult>.Fail(deriveFailure.Error);
                }

                IReadOnlyCollection<Namespace> derivedNamespaces =
                    ((Result<IReadOnlyCollection<Namespace>>.Success)deriveResult).Value;

                UpdateCredential(credential, accountName, command.Token, baseUrl);

                try
                {
                    affectedRepositories = await rotationService.RotateAsync(
                        credential,
                        derivedNamespaces,
                        cancellationToken);
                }
                catch (DbUpdateException ex) when (AccountsDatabaseHelpers.IsNamespaceDuplicateViolation(ex))
                {
                    return Result<CredentialUpdateResult>.Fail(CredentialErrors.DuplicateNamespace(credential.Host));
                }
            }
            else
            {
                UpdateCredential(credential, accountName, token: null, baseUrl);

                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (AccountsDatabaseHelpers.IsNamespaceDuplicateViolation(ex))
                {
                    return Result<CredentialUpdateResult>.Fail(CredentialErrors.DuplicateNamespace(credential.Host));
                }
            }

            string providerType = isGitLab ? ProviderTypes.GitLab : ProviderTypes.GitHub;
            CredentialSummary summary = new(
                credential.Id.Value,
                credential.Name,
                providerType,
                credential.BaseUrl.Value.ToString(),
                credential.Token is not null,
                credential.Namespaces.Select(n => n.Value).ToList());

            return Result<CredentialUpdateResult>.Ok(new CredentialUpdateResult(summary, affectedRepositories));
        }

        /// <summary>
        /// Derives namespaces for the new token and guards against duplicate-account and
        /// transient-unavailability failures — all before any state mutation.
        /// Returns the derived namespaces on success, or the rejection <see cref="Error"/> on failure.
        /// </summary>
        private async Task<Result<IReadOnlyCollection<Namespace>>> DeriveAndGuardAsync(
            string accountName,
            Guid excludeCredentialId,
            Uri apiBaseUrl,
            string token,
            BaseUrlVo baseUrl,
            bool isGitLab,
            CancellationToken cancellationToken)
        {
            NamespaceDerivationOutcome outcome = await namespaceDeriver.DeriveAsync(
                apiBaseUrl,
                token,
                isGitLab,
                cancellationToken);

            if (outcome is not NamespaceDerivationOutcome.Derived derived)
            {
                return Result<IReadOnlyCollection<Namespace>>.Fail(CredentialErrors.NamespaceDerivationUnavailable);
            }

            IReadOnlyCollection<Namespace> derivedNamespaces = derived.Namespaces;

            Dictionary<string, (Guid HolderCredentialId, string HolderName)> claimedByOthers =
                await dbContext.FindClaimedNamespacesAsync(
                    baseUrl.Value.Host,
                    excludingCredentialId: excludeCredentialId,
                    cancellationToken);

            if (DuplicateAccount.Find(accountName, derivedNamespaces, claimedByOthers) is (string holderName, string sharedOwner))
            {
                return Result<IReadOnlyCollection<Namespace>>.Fail(
                    CredentialErrors.DuplicateAccount(holderName, sharedOwner));
            }

            return Result<IReadOnlyCollection<Namespace>>.Ok(derivedNamespaces);
        }

        private static void UpdateCredential(Credential credential, string accountName, string? token, BaseUrlVo baseUrl)
        {
            switch (credential)
            {
                case GitHubCredential gitHubCredential:
                    gitHubCredential.Update(accountName, token, baseUrl);
                    break;
                case GitLabCredential gitLabCredential:
                    gitLabCredential.Update(accountName, token, baseUrl);
                    break;
                default:
                    throw new UnreachableException(
                        $"No Update handler for credential type '{credential.GetType().Name}'.");
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
                    ICommandHandler<Command, CredentialUpdateResult> handler,
                    CancellationToken cancellationToken) =>
                {
                    CredentialId credentialId = CredentialId.From(id);
                    Command command = new(credentialId, body.BaseUrl, body.Token);
                    Result<CredentialUpdateResult> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<CredentialUpdateResult>, NotFound, Conflict<string>, BadRequest<string>>>(
                        updateResult => TypedResults.Ok(updateResult),
                        error => error.Code switch
                        {
                            CredentialErrors.NotFoundCode => TypedResults.NotFound(),
                            CredentialErrors.DuplicateNamespaceCode => TypedResults.Conflict(error.Message),
                            CredentialErrors.DuplicateAccountCode => TypedResults.Conflict(error.Message),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("UpdateAccount")
                .WithSummary("Updates an existing account")
                .Produces<CredentialUpdateResult>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .Produces<string>(StatusCodes.Status409Conflict)
                .Produces<string>(StatusCodes.Status400BadRequest);
        }
    }
}
