using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features;

internal sealed class BranchProtectionValidator(
    DbContext dbContext,
    IProviderAuth providerAuth,
    IIssueProviderFactory providerFactory) : IBranchProtectionValidator
{
    public async Task<Result<IReadOnlyList<EligibilityViolationInfo>>> ValidateAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
    {
        MonitoredRepository? repo = await dbContext.Set<MonitoredRepository>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);

        if (repo is null)
        {
            return Result<IReadOnlyList<EligibilityViolationInfo>>.Fail(
                BranchProtectionValidatorErrors.RepositoryNotFound(repositoryId));
        }

        Account? account = await dbContext.Set<Account>()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == repo.AccountId, cancellationToken);

        if (account is null)
        {
            return Result<IReadOnlyList<EligibilityViolationInfo>>.Fail(
                BranchProtectionValidatorErrors.AccountNotFound(repo.AccountId));
        }

        Result<string> tokenResult = await providerAuth.GetTokenAsync(
            account.SecretKeyName,
            cancellationToken);

        if (tokenResult is not Result<string>.Success tokenSuccess)
        {
            Error error = ((Result<string>.Failure)tokenResult).Error;
            return Result<IReadOnlyList<EligibilityViolationInfo>>.Fail(error);
        }

        IIssueProvider provider = providerFactory.CreateProvider(account, tokenSuccess.Value);

        Result<BranchProtection> protectionResult = await provider.GetBranchProtectionAsync(
            repo.Slug,
            cancellationToken);

        if (protectionResult is not Result<BranchProtection>.Success protectionSuccess)
        {
            return Result<IReadOnlyList<EligibilityViolationInfo>>.Ok(
                [new EligibilityViolationInfo(
                    EligibilityViolationInfo.UnreachableRule,
                    EligibilityViolationInfo.UnreachableDescription)]);
        }

        BranchProtection protection = protectionSuccess.Value;
        List<EligibilityViolationInfo> violations = [];

        if (!protection.RejectDirectPushes)
        {
            violations.Add(new EligibilityViolationInfo(
                EligibilityViolationInfo.AllowDirectPushesRule,
                "The repository allows direct pushes to the protected branch, which could allow bypassing the worker's pull request workflow."));
        }

        if (!protection.RejectForcePushes)
        {
            violations.Add(new EligibilityViolationInfo(
                EligibilityViolationInfo.AllowForcePushesRule,
                "The repository allows force pushes to the protected branch, which could allow overwriting the worker's commits."));
        }

        if (!protection.RejectDeletion)
        {
            violations.Add(new EligibilityViolationInfo(
                EligibilityViolationInfo.AllowDeletionRule,
                "The repository allows deletion of the protected branch, which could result in loss of the worker's work."));
        }

        return Result<IReadOnlyList<EligibilityViolationInfo>>.Ok(violations);
    }
}

internal static class BranchProtectionValidatorErrors
{
    public static Error RepositoryNotFound(MonitoredRepositoryId id) =>
        new("BranchProtectionValidator.RepositoryNotFound",
            $"No monitored repository found with id '{id.Value}'.");

    public static Error AccountNotFound(AccountId id) =>
        new("BranchProtectionValidator.AccountNotFound",
            $"No account found with id '{id.Value}'.");
}
