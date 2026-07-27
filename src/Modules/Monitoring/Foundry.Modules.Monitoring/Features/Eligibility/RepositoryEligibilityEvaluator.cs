using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Eligibility;

internal sealed class RepositoryEligibilityEvaluator(
    ICredentialResolver credentialResolver,
    IIssueProviderFactory providerFactory,
    IGitHubWriteProber gitHubWriteProber,
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

        RepositoryEligibility eligibility;

        try
        {
            eligibility = credential is GitHubCredential gitHubCredential
                ? await EvaluateGitHubEligibilityAsync(repo, gitHubCredential, token, cancellationToken)
                : await EvaluateProviderEligibilityAsync(repo, credential, token, cancellationToken);
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

    private async Task<RepositoryEligibility> EvaluateGitHubEligibilityAsync(
        MonitoredRepository repo,
        GitHubCredential credential,
        string token,
        CancellationToken cancellationToken)
    {
        Result<WritePermissionProbeResult> probeResult = await gitHubWriteProber.ProbeWriteAccessAsync(
            credential.ApiBaseUrl,
            repo.Slug,
            token,
            cancellationToken);

        if (probeResult is not Result<WritePermissionProbeResult>.Success { Value: WritePermissionProbeResult probe })
        {
            return new RepositoryEligibility.Unreachable();
        }

        if (probe is WritePermissionProbeResult.Missing)
        {
            return new RepositoryEligibility.Ineligible([EligibilityViolation.CannotPush(repo.Slug)]);
        }

        IIssueProvider provider = providerFactory.CreateProvider(credential, token);
        return EvaluateEligibility(await provider.GetBranchProtectionAsync(repo.Slug, cancellationToken));
    }

    private async Task<RepositoryEligibility> EvaluateProviderEligibilityAsync(
        MonitoredRepository repo,
        Credential credential,
        string token,
        CancellationToken cancellationToken)
    {
        IIssueProvider provider = providerFactory.CreateProvider(credential, token);

        Result<bool> canPushResult = await provider.CanPushAsync(repo.Slug, cancellationToken);

        return canPushResult switch
        {
            Result<bool>.Failure => new RepositoryEligibility.Unreachable(),
            Result<bool>.Success { Value: false } => new RepositoryEligibility.Ineligible(
                [EligibilityViolation.CannotPush(repo.Slug)]),
            _ => EvaluateEligibility(await provider.GetBranchProtectionAsync(repo.Slug, cancellationToken)),
        };
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
