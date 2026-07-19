using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class RecheckRepositoryEligibility
{
    internal sealed record Command(Guid AccountId, Guid Id) : ICommand<RepositorySummary>;

    // NOTE: 5 constructor dependencies — exceeds the 4-cap, but ILogger takes priority over operational
    // visibility. GitHubHttpClient and GitLabHttpClient cannot be consolidated without a shared interface.
    internal sealed class Handler(
        DbContext dbContext,
        IRepositoryEligibilityEvaluator eligibilityEvaluator,
        GitHubHttpClient gitHubHttpClient,
        GitLabHttpClient gitLabHttpClient,
        ILogger<Handler> logger) : ICommandHandler<Command, RepositorySummary>
    {
        public async Task<Result<RepositorySummary>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            CredentialId credentialId = CredentialId.From(command.AccountId);
            MonitoredRepositoryId repositoryId = MonitoredRepositoryId.From(command.Id);

            MonitoredRepository? repository = await dbContext.Set<MonitoredRepository>()
                .FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);

            if (repository is null)
            {
                return Result<RepositorySummary>.Fail(RepositoryErrors.NotFound(repositoryId));
            }

            Credential? credential = await dbContext.Set<Credential>()
                .Include(c => c.Namespaces)
                .FirstOrDefaultAsync(a => a.Id == credentialId, cancellationToken);

            if (credential is null)
            {
                return Result<RepositorySummary>.Fail(RepositoryErrors.AccountNotFound(credentialId));
            }

            // Refresh the credential's namespace set from the live writable-repo listing so that
            // newly-granted namespaces become covered before re-evaluating eligibility.
            await RefreshNamespacesAsync(credential, cancellationToken);

            await eligibilityEvaluator.EvaluateAndStoreAsync(repository, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            RepositorySummary summary = new(
                repository.Id.Value,
                repository.Slug.ToString(),
                credential.Id.Value,
                credential.Name,
                credential switch
                {
                    GitHubCredential => ProviderTypes.GitHub,
                    GitLabCredential => ProviderTypes.GitLab,
                    _ => throw new UnreachableException(),
                },
                RepositoryMappings.ToSeconds(repository.PollInterval),
                repository.IsActive,
                repository.LastPolledAt,
                RepositoryMappings.ToEligibilityInfo(repository.Eligibility),
                repository.Position);

            return Result<RepositorySummary>.Ok(summary);
        }

        private async Task RefreshNamespacesAsync(Credential credential, CancellationToken cancellationToken)
        {
            if (credential.Token is null)
            {
                return;
            }

            try
            {
                bool isGitLab = credential is GitLabCredential;
                Result<IReadOnlyList<AvailableRepository>> listResult = isGitLab
                    ? await gitLabHttpClient.ListRepositoriesAsync(credential.ApiBaseUrl, credential.Token, cancellationToken)
                    : await gitHubHttpClient.ListRepositoriesAsync(credential.ApiBaseUrl, credential.Token, cancellationToken);

                if (listResult is not Result<IReadOnlyList<AvailableRepository>>.Success listSuccess)
                {
                    return;
                }

                IReadOnlyCollection<Namespace> namespaces = NamespaceDerivation.FromWritableRepositories(listSuccess.Value);
                credential.SetNamespaces(namespaces);
            }
#pragma warning disable CA1031 // Provider listing may fail with any exception — leave existing namespaces unchanged on recheck
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogWarning(
                    ex,
                    "Namespace refresh failed for credential {CredentialId}; evaluating eligibility against cached namespaces.",
                    credential.Id);
            }
        }
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("{id:guid}/recheck", static async (
                    Guid accountId,
                    Guid id,
                    ICommandHandler<Command, RepositorySummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(accountId, id);
                    Result<RepositorySummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<RepositorySummary>, NotFound<string>, UnprocessableEntity<string>, ProblemHttpResult>>(
                        repository => TypedResults.Ok(repository),
                        error => error.Code switch
                        {
                            RepositoryErrors.NotFoundCode => TypedResults.NotFound(error.Message),
                            RepositoryErrors.AccountNotFoundCode => TypedResults.NotFound(error.Message),
                            RepositoryErrors.NoTokenCode => TypedResults.UnprocessableEntity(error.Message),
                            _ => TypedResults.Problem(error.Message),
                        });
                })
                .WithName("RecheckRepositoryEligibility")
                .WithSummary("Re-evaluates branch protection eligibility for a monitored repository")
                .Produces<RepositorySummary>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }
    }
}
