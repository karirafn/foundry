using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Eligibility;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Accounts.Rotation;

/// <summary>
/// Applies Foundry's default-branch rules to every repository an account covers whose persisted
/// <see cref="MonitoredRepository.ApplyBranchRules"/> intent is <see langword="true"/>, after the
/// account's branch-rules token is set or replaced (ADR 0084).
/// </summary>
/// <remarks>
/// Repositories are processed sequentially — the DbContext is not thread-safe and the provider
/// writes must not run in parallel. A failure on one repository is logged and never stops the
/// others or fails the account update, which is already committed before this runs.
/// </remarks>
internal sealed class OptedInBranchRuleApplier(
    DbContext dbContext,
    RepositoryEligibilityDiffer differ,
    BranchRuleEnforcer enforcer,
    ILogger<OptedInBranchRuleApplier> logger)
{
    private const string UnreachableStatus = "unreachable";

    /// <summary>
    /// Runs the enforcer for the opted-in covered repositories and folds the resulting eligibility
    /// changes into <paramref name="affectedRepositories"/>, so the returned list reports each
    /// repository's status before the update against its status after enforcement.
    /// </summary>
    public async Task<IReadOnlyList<AffectedRepository>> ApplyAsync(
        Credential credential,
        IReadOnlyList<AffectedRepository> affectedRepositories,
        CancellationToken cancellationToken)
    {
        List<MonitoredRepository> optedIn = (await differ.FindResolvingReposAsync(credential, cancellationToken))
            .Where(r => r.ApplyBranchRules)
            .ToList();

        if (optedIn.Count == 0)
        {
            return affectedRepositories;
        }

        // A repository already reported by the rotation keeps its pre-update status as the baseline;
        // any other repository's eligibility was unchanged by the update, so its current status is.
        Dictionary<Guid, string> previousStatus = optedIn.ToDictionary(
            r => r.Id.Value,
            r => r.EligibilityStatus ?? UnreachableStatus);
        foreach (AffectedRepository affected in affectedRepositories)
        {
            previousStatus[affected.Id] = affected.PreviousStatus;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (MonitoredRepository repository in optedIn)
        {
            await ApplyToRepositoryAsync(repository, credential, now, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        Dictionary<Guid, MonitoredRepository> optedInById = optedIn.ToDictionary(r => r.Id.Value);
        List<AffectedRepository> result = [];
        foreach (AffectedRepository affected in affectedRepositories)
        {
            if (!optedInById.TryGetValue(affected.Id, out MonitoredRepository? repository))
            {
                result.Add(affected);
                continue;
            }

            AddIfChanged(result, repository, affected.PreviousStatus);
            optedInById.Remove(affected.Id);
        }

        foreach (MonitoredRepository repository in optedIn.Where(r => optedInById.ContainsKey(r.Id.Value)))
        {
            AddIfChanged(result, repository, previousStatus[repository.Id.Value]);
        }

        return result;
    }

    private async Task ApplyToRepositoryAsync(
        MonitoredRepository repository,
        Credential credential,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            BranchRulesOutcome outcome = await enforcer.EvaluateAndApplyAsync(
                repository,
                credential,
                applyBranchRules: true,
                now,
                cancellationToken);

            logger.LogInformation(
                "Branch rules for repository {Slug} after the branch-rules token was saved: {Kind} ({Reason}).",
                repository.Slug,
                outcome.Kind,
                outcome.Reason);
        }
#pragma warning disable CA1031 // One repository's failure must never stop the others or fail the account update
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(
                ex,
                "Applying branch rules to repository {Slug} after the branch-rules token was saved threw.",
                repository.Slug);
        }
    }

    private static void AddIfChanged(List<AffectedRepository> result, MonitoredRepository repository, string previous)
    {
        string current = repository.EligibilityStatus ?? UnreachableStatus;
        if (previous != current)
        {
            result.Add(new AffectedRepository(repository.Id.Value, repository.Slug.FullPath, previous, current));
        }
    }
}
