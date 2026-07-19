using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features;

internal sealed class RepositoryEligibilityEvaluator(
    ICredentialResolver credentialResolver,
    IIssueProviderFactory providerFactory,
    ILogger<RepositoryEligibilityEvaluator> logger) : IRepositoryEligibilityEvaluator
{
    public async Task EvaluateAndStoreAsync(
        MonitoredRepository repo,
        CancellationToken cancellationToken)
    {
        Credential? credential = await credentialResolver.ResolveAsync(
            repo.Host,
            repo.Slug,
            cancellationToken);

        if (credential is null)
        {
            string topLevelNamespace = Namespace.PrefixesOf(repo.Slug)[^1].Value;
            repo.SetEligibility(new RepositoryEligibility.Ineligible(
                [EligibilityViolation.NoCredential(topLevelNamespace)]));
            return;
        }

        string token = credential.Token ?? string.Empty;
        IIssueProvider provider = providerFactory.CreateProvider(credential, token);

        RepositoryEligibility eligibility;

        try
        {
            Result<bool> canPushResult = await provider.CanPushAsync(repo.Slug, cancellationToken);

            if (canPushResult is Result<bool>.Failure)
            {
                repo.SetEligibility(new RepositoryEligibility.Unreachable());
                return;
            }

            if (canPushResult is Result<bool>.Success { Value: false })
            {
                repo.SetEligibility(new RepositoryEligibility.Ineligible(
                    [EligibilityViolation.CannotPush(repo.Slug)]));
                return;
            }

            Result<BranchProtection> result = await provider.GetBranchProtectionAsync(
                repo.Slug,
                cancellationToken);

            eligibility = EvaluateEligibility(result);
        }
#pragma warning disable CA1031 // Provider calls may fail with any exception type (network, serialization, etc.) — treat all as unreachable
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(
                ex,
                "Failed to evaluate eligibility for repository {Slug}; marking as unreachable.",
                repo.Slug);
            eligibility = new RepositoryEligibility.Unreachable();
        }

        repo.SetEligibility(eligibility);
    }

    private static RepositoryEligibility EvaluateEligibility(Result<BranchProtection> result)
    {
        if (result is not Result<BranchProtection>.Success success)
        {
            return new RepositoryEligibility.Unreachable();
        }

        BranchProtection protection = success.Value;
        List<EligibilityViolation> violations = [];

        if (!protection.RejectDirectPushes)
        {
            violations.Add(EligibilityViolation.AllowDirectPushes());
        }

        if (!protection.RejectForcePushes)
        {
            violations.Add(EligibilityViolation.AllowForcePushes());
        }

        if (!protection.RejectDeletion)
        {
            violations.Add(EligibilityViolation.AllowDeletion());
        }

        if (violations.Count > 0)
        {
            return new RepositoryEligibility.Ineligible(violations);
        }

        return new RepositoryEligibility.Eligible();
    }
}
