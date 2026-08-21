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

    /// <summary>
    /// Discriminated union returned by the update handler to carry structured
    /// conflict/validation payloads without encoding them into Error.Message.
    /// </summary>
    internal abstract class Outcome
    {
        private Outcome() { }

        internal sealed class Updated(CredentialUpdateResult value) : Outcome
        {
            public CredentialUpdateResult Value { get; } = value;
        }

        internal sealed class ClaimedElsewhere(NamespaceClaimedElsewhereResponse response) : Outcome
        {
            public NamespaceClaimedElsewhereResponse Response { get; } = response;
        }

        internal sealed class Rejected(Error error) : Outcome
        {
            public Error Error { get; } = error;
        }
    }

    /// <summary>
    /// Three-way result from <see cref="Handler.DeriveAndGuardAsync"/>, separating the
    /// success (derived namespaces ready to use), fully-claimed-by-others rejection, and
    /// error-rejection paths without encoding them into <see cref="Error.Message"/>.
    /// </summary>
    private abstract class DeriveGuardResult
    {
        private DeriveGuardResult() { }

        internal sealed class Derived(IReadOnlyCollection<Namespace> namespaces) : DeriveGuardResult
        {
            public IReadOnlyCollection<Namespace> Namespaces { get; } = namespaces;
        }

        internal sealed class ClaimedElsewhere(NamespaceClaimedElsewhereResponse response) : DeriveGuardResult
        {
            public NamespaceClaimedElsewhereResponse Response { get; } = response;
        }

        internal sealed class Rejected(Error error) : DeriveGuardResult
        {
            public Error Error { get; } = error;
        }
    }

    internal sealed class Handler(
        DbContext dbContext,
        IQueryHandler<ValidateToken.Query, ValidateToken.Response> validateToken,
        INamespaceDeriver namespaceDeriver,
        CredentialRotationService rotationService)
    {
        public async Task<Outcome> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            if (await dbContext.Set<Credential>()
                    .Include(c => c.Namespaces)
                    .FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken)
                is not Credential credential)
            {
                return new Outcome.Rejected(CredentialErrors.NotFound(command.Id));
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
                    return new Outcome.Rejected(tokenFailure.Error);
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
                    return new Outcome.Rejected(kindError);
                }

                if (string.IsNullOrWhiteSpace(resolvedName))
                {
                    return new Outcome.Rejected(CredentialErrors.UnresolvedIdentity);
                }

                if (resolvedName.Length > AccountsDatabaseHelpers.AccountNameMaxLength || resolvedName.Any(char.IsControl))
                {
                    return new Outcome.Rejected(CredentialErrors.UnresolvedIdentity);
                }

                accountName = resolvedName;

                DeriveGuardResult deriveResult = await DeriveAndGuardAsync(
                    accountName,
                    credential.Id.Value,
                    apiBaseUrl,
                    command.Token!,
                    baseUrl,
                    isGitLab,
                    cancellationToken);

                if (deriveResult is DeriveGuardResult.Rejected deriveRejected)
                {
                    return new Outcome.Rejected(deriveRejected.Error);
                }

                if (deriveResult is DeriveGuardResult.ClaimedElsewhere deriveClaimedElsewhere)
                {
                    return new Outcome.ClaimedElsewhere(deriveClaimedElsewhere.Response);
                }

                if (deriveResult is not DeriveGuardResult.Derived derivedResult)
                {
                    throw new UnreachableException($"Unhandled DeriveGuardResult: {deriveResult.GetType().Name}");
                }

                IReadOnlyCollection<Namespace> derivedNamespaces = derivedResult.Namespaces;

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
                    return new Outcome.Rejected(CredentialErrors.DuplicateNamespace(credential.Host));
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
                    return new Outcome.Rejected(CredentialErrors.DuplicateNamespace(credential.Host));
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

            return new Outcome.Updated(new CredentialUpdateResult(summary, affectedRepositories));
        }

        /// <summary>
        /// Derives namespaces for the new token and guards against duplicate-account,
        /// fully-claimed-by-other-logins, and transient-unavailability failures — all before any state mutation.
        /// </summary>
        private async Task<DeriveGuardResult> DeriveAndGuardAsync(
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
                return new DeriveGuardResult.Rejected(CredentialErrors.NamespaceDerivationUnavailable);
            }

            IReadOnlyCollection<Namespace> derivedNamespaces = derived.Namespaces;

            Dictionary<string, (Guid HolderCredentialId, string HolderName)> claimedByOthers =
                await dbContext.FindClaimedNamespacesAsync(
                    baseUrl.Value.Host,
                    excludingCredentialId: excludeCredentialId,
                    cancellationToken);

            bool allDerivedClaimedByOthers = derivedNamespaces.All(ns => claimedByOthers.TryGetValue(ns.Value, out _));

            if (DuplicateAccount.Find(accountName, derivedNamespaces, claimedByOthers) is (string holderName, string sharedOwner))
            {
                // Reject only when the rotation would strand the credential on zero namespaces
                // because the sibling already covers the entire derived set. When a retained set
                // remains, RotateAsync's never-steal subtraction will reduce to it correctly.
                if (allDerivedClaimedByOthers)
                {
                    return new DeriveGuardResult.Rejected(
                        CredentialErrors.DuplicateAccount(holderName, sharedOwner));
                }
            }

            // Reject when the token's entire (non-empty) derived owner set is already claimed by
            // OTHER credentials — rotating would strand this account on zero namespace claims.
            bool derivedIsNonEmpty = derivedNamespaces.Count > 0;

            if (derivedIsNonEmpty && allDerivedClaimedByOthers)
            {
                List<NamespaceConflict> conflicts = derivedNamespaces
                    .OrderBy(ns => ns.Value, StringComparer.Ordinal)
                    .Select(ns =>
                    {
                        claimedByOthers.TryGetValue(ns.Value, out (Guid HolderCredentialId, string HolderName) holder);
                        return new NamespaceConflict(ns.Value, holder.HolderCredentialId, holder.HolderName);
                    })
                    .ToList();

                return new DeriveGuardResult.ClaimedElsewhere(new NamespaceClaimedElsewhereResponse(conflicts));
            }

            return new DeriveGuardResult.Derived(derivedNamespaces);
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
                    Handler handler,
                    ICommandValidator<Command> validator,
                    CancellationToken cancellationToken) =>
                {
                    CredentialId credentialId = CredentialId.From(id);
                    Command command = new(credentialId, body.BaseUrl, body.Token);

                    Result validation = validator.Validate(command);
                    if (validation is Result.Failure validationFailure)
                    {
                        return (IResult)TypedResults.BadRequest(validationFailure.Error.Message);
                    }

                    Outcome outcome = await handler.HandleAsync(command, cancellationToken);

                    return outcome switch
                    {
                        Outcome.Updated updated =>
                            TypedResults.Ok(updated.Value),
                        Outcome.ClaimedElsewhere claimedElsewhere =>
                            (IResult)TypedResults.Conflict(claimedElsewhere.Response),
                        Outcome.Rejected rejected => rejected.Error.Code switch
                        {
                            CredentialErrors.NotFoundCode => (IResult)TypedResults.NotFound(),
                            CredentialErrors.DuplicateNamespaceCode => TypedResults.Conflict(rejected.Error.Message),
                            CredentialErrors.DuplicateAccountCode => TypedResults.Conflict(rejected.Error.Message),
                            _ => TypedResults.BadRequest(rejected.Error.Message),
                        },
                        _ => throw new UnreachableException(
                            $"Unhandled UpdateAccount.Outcome: {outcome.GetType().Name}"),
                    };
                })
                .WithName("UpdateAccount")
                .WithSummary("Updates an existing account")
                .Produces<CredentialUpdateResult>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .Produces<NamespaceClaimedElsewhereResponse>(StatusCodes.Status409Conflict)
                .Produces<string>(StatusCodes.Status400BadRequest);
        }
    }
}
