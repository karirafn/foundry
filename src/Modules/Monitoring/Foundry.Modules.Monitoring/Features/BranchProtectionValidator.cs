using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features;

internal sealed class BranchProtectionValidator(
    DbContext dbContext,
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

        if (string.IsNullOrEmpty(account.Token))
        {
            return Result<IReadOnlyList<EligibilityViolationInfo>>.Fail(
                BranchProtectionValidatorErrors.AccountTokenNotConfigured(account.Id));
        }

        IIssueProvider provider = providerFactory.CreateProvider(account, account.Token);

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
                EligibilityViolationInfo.AllowDirectPushesDescription));
        }

        if (!protection.RejectForcePushes)
        {
            violations.Add(new EligibilityViolationInfo(
                EligibilityViolationInfo.AllowForcePushesRule,
                EligibilityViolationInfo.AllowForcePushesDescription));
        }

        if (!protection.RejectDeletion)
        {
            violations.Add(new EligibilityViolationInfo(
                EligibilityViolationInfo.AllowDeletionRule,
                EligibilityViolationInfo.AllowDeletionDescription));
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

    public static Error AccountTokenNotConfigured(AccountId id) =>
        new("BranchProtectionValidator.AccountTokenNotConfigured",
            $"Account with id '{id.Value}' has no token configured.");
}
