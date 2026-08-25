using System.Diagnostics;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Services;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts.Rotation;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
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

    internal sealed class Handler(
        DbContext dbContext,
        TokenAccountResolver tokenAccountResolver,
        CredentialRotationService rotationService,
        ProviderHostGuard hostGuard)
    {
        public async Task<Outcome> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            if (BaseUrlVo.Create(command.BaseUrl) is not Result<BaseUrlVo>.Success { Value: BaseUrlVo baseUrl })
            {
                throw new UnreachableException("BaseUrl validated in the validator but failed in the handler.");
            }

            Result hostGuardResult = await hostGuard.EnsureAllowedAsync(baseUrl, cancellationToken);
            if (hostGuardResult is Result.Failure hostGuardFailure)
            {
                return new Outcome.Rejected(hostGuardFailure.Error);
            }

            if (await dbContext.Set<Credential>()
                    .Include(c => c.Namespaces)
                    .FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken)
                is not Credential credential)
            {
                return new Outcome.Rejected(CredentialErrors.NotFound(command.Id));
            }

            bool isGitLab = credential is GitLabCredential;
            string accountName = credential.Name;

            IReadOnlyList<AffectedRepository> affectedRepositories = [];

            if (command.Token is not null)
            {
                TokenResolution resolution = await tokenAccountResolver.ResolveAsync(
                    credential,
                    command.Token,
                    baseUrl,
                    isGitLab,
                    cancellationToken);

                if (resolution is TokenResolution.Rejected rejected)
                {
                    return new Outcome.Rejected(rejected.Error);
                }

                if (resolution is TokenResolution.ClaimedElsewhere claimed)
                {
                    return new Outcome.ClaimedElsewhere(claimed.Response);
                }

                if (resolution is not TokenResolution.Resolved resolved)
                {
                    throw new UnreachableException(
                        $"Unhandled TokenResolution: {resolution.GetType().Name}");
                }

                accountName = resolved.AccountName;

                UpdateCredential(credential, accountName, command.Token, baseUrl);

                try
                {
                    affectedRepositories = await rotationService.RotateAsync(
                        credential,
                        resolved.Namespaces,
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
