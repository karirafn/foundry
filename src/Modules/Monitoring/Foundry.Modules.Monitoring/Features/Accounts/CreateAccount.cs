using System.Diagnostics;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts.Rotation;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;

using BaseUrlVo = Foundry.Modules.Monitoring.Domain.ValueObjects.BaseUrl;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static partial class CreateAccount
{
    internal sealed record Command(
        string ProviderType,
        string BaseUrl,
        string Token,
        IReadOnlyList<string>? TakeoverNamespaces = null) : ICommand<CredentialCreationResult>;

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

    /// <summary>
    /// Discriminated union returned by the create handler to carry structured
    /// conflict/validation payloads without encoding them into Error.Message.
    /// </summary>
    internal abstract class Outcome
    {
        private Outcome() { }

        internal sealed class Created(CredentialCreationResult value) : Outcome
        {
            public CredentialCreationResult Value { get; } = value;
        }

        internal sealed class Conflict(NamespaceConflictResponse conflicts) : Outcome
        {
            public NamespaceConflictResponse Conflicts { get; } = conflicts;
        }

        internal sealed class InvalidTakeover(TakeoverValidationResponse invalid) : Outcome
        {
            public TakeoverValidationResponse Invalid { get; } = invalid;
        }

        internal sealed class Failure(Error error) : Outcome
        {
            public Error Error { get; } = error;
        }
    }

    internal sealed class Handler(
        DbContext dbContext,
        IQueryHandler<ValidateToken.Query, ValidateToken.Response> validateToken,
        INamespaceDeriver namespaceDeriver,
        RepositoryEligibilityDiffer differ,
        IGitHubWriteProber writeProber)
    {
        public async Task<Outcome> HandleAsync(
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
                return new Outcome.Failure(error);
            }

            ValidateToken.Response tokenResponse = tokenSuccess.Value;
            if (tokenResponse.Kind != ValidateToken.Kinds.Authenticated)
            {
                return new Outcome.Failure(CredentialErrors.InvalidToken);
            }

            if (string.IsNullOrWhiteSpace(tokenResponse.AccountName))
            {
                return new Outcome.Failure(CredentialErrors.UnresolvedIdentity);
            }

            string accountName = tokenResponse.AccountName;

            if (accountName.Length > AccountsDatabaseHelpers.AccountNameMaxLength || accountName.Any(char.IsControl))
            {
                return new Outcome.Failure(CredentialErrors.UnresolvedIdentity);
            }

            Credential credential = isGitLab
                ? GitLabCredential.Create(accountName, command.Token, baseUrl)
                : GitHubCredential.Create(accountName, command.Token, baseUrl);

            NamespaceDerivationOutcome derivationOutcome = await namespaceDeriver.DeriveAsync(credential, cancellationToken);
            IReadOnlyCollection<Namespace> derivedNamespaces;
            IReadOnlyList<ProviderRepository> writableRepositories;

            if (derivationOutcome is NamespaceDerivationOutcome.Derived derivedOutcome)
            {
                derivedNamespaces = derivedOutcome.Namespaces;
                writableRepositories = derivedOutcome.WritableRepositories;
            }
            else
            {
                derivedNamespaces = [];
                writableRepositories = [];
            }

            if (!isGitLab)
            {
                Outcome? probeFailure = await ProbeWriteAccessAsync(
                    apiBaseUrl,
                    derivedNamespaces,
                    writableRepositories,
                    command.Token,
                    cancellationToken);

                if (probeFailure is not null)
                {
                    return probeFailure;
                }
            }

            // Compute conflicts: namespaces in the derived set already claimed by another credential
            Dictionary<string, (Guid HolderCredentialId, string HolderName)> claimedByOthers =
                await dbContext.FindClaimedNamespacesAsync(credential.Host, cancellationToken: cancellationToken);

            HashSet<string> derivedValues = derivedNamespaces
                .Select(n => n.Value)
                .ToHashSet(StringComparer.Ordinal);

            List<NamespaceConflict> conflicts = [];
            foreach (KeyValuePair<string, (Guid HolderCredentialId, string HolderName)> entry in claimedByOthers)
            {
                if (derivedValues.Contains(entry.Key))
                {
                    conflicts.Add(new NamespaceConflict(entry.Key, entry.Value.HolderCredentialId, entry.Value.HolderName));
                }
            }

            if (command.TakeoverNamespaces is { Count: > 0 } takeoverList)
            {
                // Validate: every requested takeover namespace must be in the derived set
                List<string> invalidNamespaces = takeoverList
                    .Where(ns => !derivedValues.Contains(ns))
                    .ToList();

                if (invalidNamespaces.Count > 0)
                {
                    return new Outcome.InvalidTakeover(new TakeoverValidationResponse(invalidNamespaces));
                }

                HashSet<string> takeoverSet = takeoverList.ToHashSet(StringComparer.Ordinal);

                // Snapshot prior eligibility of repos covered by the namespaces being taken over,
                // before any holder rows are deleted, so DiffAsync can reflect the true prior status.
                Dictionary<Guid, string> priorStatusSnapshot =
                    await SnapshotPriorEligibilityAsync(credential.Host, takeoverSet, cancellationToken);

                // Execute takeover in one explicit transaction:
                // delete transferred holder rows + insert the new credential
                await using IDbContextTransaction transaction =
                    await dbContext.Database.BeginTransactionAsync(cancellationToken);

                // Delete CredentialNamespace rows from holders for all takeover namespaces
                List<CredentialNamespace> holderRows = await dbContext.Set<CredentialNamespace>()
                    .Where(n => n.Host == credential.Host)
                    .ToListAsync(cancellationToken);

                List<CredentialNamespace> rowsToDelete = holderRows
                    .Where(n => takeoverSet.Contains(n.Value))
                    .ToList();

                if (rowsToDelete.Count > 0)
                {
                    dbContext.Set<CredentialNamespace>().RemoveRange(rowsToDelete);
                }

                // Insert the new credential with only the namespaces not still owned by others
                // (i.e. the derived set minus any claimed namespace not in the takeover list).
                HashSet<string> claimedExcludingTakeover = claimedByOthers.Keys
                    .Where(k => !takeoverSet.Contains(k))
                    .ToHashSet(StringComparer.Ordinal);
                credential.SetNamespaces(derivedNamespaces, claimedExcludingTakeover);
                dbContext.Set<Credential>().Add(credential);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                // After the transaction, re-evaluate repos under the transferred namespaces
                // and return them as affected repositories.
                IReadOnlyList<AffectedRepository> affectedRepositories = await RecheckTransferredReposAsync(
                    credential,
                    priorStatusSnapshot,
                    cancellationToken);

                string providerTypeAfterTakeover = isGitLab ? ProviderTypes.GitLab : ProviderTypes.GitHub;
                CredentialSummary summaryAfterTakeover = new(
                    credential.Id.Value,
                    credential.Name,
                    providerTypeAfterTakeover,
                    credential.BaseUrl.Value.ToString(),
                    credential.Token is not null,
                    credential.Namespaces.Select(n => n.Value).ToList());

                return new Outcome.Created(new CredentialCreationResult(summaryAfterTakeover, affectedRepositories));
            }
            else if (conflicts.Count > 0)
            {
                // Conflicts present and no takeover requested — return structured 409
                return new Outcome.Conflict(new NamespaceConflictResponse(conflicts));
            }
            else
            {
                // No conflicts — use single implicit SaveChanges
                credential.SetNamespaces(derivedNamespaces);
                dbContext.Set<Credential>().Add(credential);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            string providerType = isGitLab ? ProviderTypes.GitLab : ProviderTypes.GitHub;
            CredentialSummary summary = new(
                credential.Id.Value,
                credential.Name,
                providerType,
                credential.BaseUrl.Value.ToString(),
                credential.Token is not null,
                credential.Namespaces.Select(n => n.Value).ToList());

            return new Outcome.Created(new CredentialCreationResult(summary, []));
        }

        private async Task<Outcome?> ProbeWriteAccessAsync(
            Uri apiBaseUrl,
            IReadOnlyCollection<Namespace> derivedNamespaces,
            IReadOnlyList<ProviderRepository> writableRepositories,
            string token,
            CancellationToken cancellationToken)
        {
            RepositorySlug? probeSlug = FindProbeTarget(derivedNamespaces, writableRepositories);

            if (probeSlug is null)
            {
                return new Outcome.Failure(CredentialErrors.NoNamespaceRepositories);
            }

            Result<WritePermissionProbeResult> probeResult = await writeProber.ProbeWriteAccessAsync(
                apiBaseUrl, probeSlug, token, cancellationToken);

            if (probeResult is not Result<WritePermissionProbeResult>.Success probeSuccess)
            {
                return new Outcome.Failure(CredentialErrors.WriteAccessVerificationFailed);
            }

            if (probeSuccess.Value is WritePermissionProbeResult.Missing missing)
            {
                string permissionName = ToDisplayName(missing.Permission);
                return new Outcome.Failure(CredentialErrors.MissingWritePermission(permissionName));
            }

            return null;
        }

        private static RepositorySlug? FindProbeTarget(
            IReadOnlyCollection<Namespace> derivedNamespaces,
            IReadOnlyList<ProviderRepository> writableRepositories)
        {
            foreach (ProviderRepository repo in writableRepositories)
            {
                if (RepositorySlug.Create(repo.Slug) is not Result<RepositorySlug>.Success { Value: RepositorySlug slug })
                {
                    continue;
                }

                bool underDerivedNamespace = derivedNamespaces.Any(ns => ns.IsPrefixOf(slug));
                if (underDerivedNamespace)
                {
                    return slug;
                }
            }

            return null;
        }

        private static string ToDisplayName(WritePermission permission) =>
            permission switch
            {
                WritePermission.Contents => "Contents",
                WritePermission.Issues => "Issues",
                WritePermission.PullRequests => "Pull requests",
                _ => permission.ToString(),
            };

        private async Task<IReadOnlyList<AffectedRepository>> RecheckTransferredReposAsync(
            Credential credential,
            Dictionary<Guid, string> priorStatus,
            CancellationToken cancellationToken)
        {
            // Find repos now covered by the new credential (under transferred namespaces)
            List<MonitoredRepository> resolvedRepos = await differ.FindResolvingReposAsync(credential, cancellationToken);

            if (resolvedRepos.Count == 0)
            {
                return [];
            }

            return await differ.DiffAsync([], resolvedRepos, priorStatus, cancellationToken);
        }

        private async Task<Dictionary<Guid, string>> SnapshotPriorEligibilityAsync(
            string host,
            HashSet<string> takeoverNamespaces,
            CancellationToken cancellationToken)
        {
            List<MonitoredRepository> repos = await dbContext.Set<MonitoredRepository>()
                .Where(r => r.Host == host)
                .ToListAsync(cancellationToken);

            Dictionary<Guid, string> snapshot = [];

            foreach (MonitoredRepository repo in repos)
            {
                bool coveredByTakeover = takeoverNamespaces.Any(ns =>
                    Namespace.Create(ns) is Result<Namespace>.Success nsResult
                    && nsResult.Value.IsPrefixOf(repo.Slug));

                if (coveredByTakeover && repo.EligibilityStatus is not null)
                {
                    snapshot[repo.Id.Value] = repo.EligibilityStatus;
                }
            }

            return snapshot;
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(
            string ProviderType,
            string BaseUrl,
            string Token,
            IReadOnlyList<string>? TakeoverNamespaces = null);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost(string.Empty, static async (
                    RequestBody body,
                    Handler handler,
                    ICommandValidator<Command> validator,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(body.ProviderType, body.BaseUrl, body.Token, body.TakeoverNamespaces);

                    Result validation = validator.Validate(command);
                    if (validation is Result.Failure validationFailure)
                    {
                        return (IResult)TypedResults.BadRequest(validationFailure.Error.Message);
                    }

                    Outcome outcome = await handler.HandleAsync(command, cancellationToken);

                    return outcome switch
                    {
                        Outcome.Created created =>
                            TypedResults.Created(
                                $"/api/accounts/{created.Value.Credential.Id}",
                                created.Value),
                        Outcome.Conflict conflict =>
                            (IResult)TypedResults.Conflict(conflict.Conflicts),
                        Outcome.InvalidTakeover invalid =>
                            TypedResults.UnprocessableEntity(invalid.Invalid),
                        Outcome.Failure failure => TypedResults.BadRequest(failure.Error.Message),
                        _ => throw new UnreachableException($"Unhandled CreateAccount.Outcome: {outcome.GetType().Name}"),
                    };
                })
                .WithName("CreateAccount")
                .WithSummary("Creates a new account")
                .Produces<CredentialCreationResult>(StatusCodes.Status201Created)
                .Produces<NamespaceConflictResponse>(StatusCodes.Status409Conflict)
                .Produces<TakeoverValidationResponse>(StatusCodes.Status422UnprocessableEntity)
                .Produces<string>(StatusCodes.Status400BadRequest);
        }
    }
}
