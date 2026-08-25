using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Eligibility;

internal sealed class RepositoryEligibilityEvaluator(
    ICredentialResolver credentialResolver,
    IIssueProviderFactory providerFactory,
    IGitHubWriteProber gitHubWriteProber,
    ILogger<RepositoryEligibilityEvaluator> logger) : IRepositoryEligibilityEvaluator
{
    private readonly EligibilityComposer _composer = new(providerFactory);

    /// <inheritdoc/>
    public async Task EvaluateFullyAndStoreAsync(
        MonitoredRepository repo,
        DateTimeOffset now,
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

        try
        {
            WriteProbeVerdict verdict = await RunWriteProbeAsync(repo, credential, token, now, cancellationToken);
            repo.SetWriteProbeVerdict(verdict);

            RepositoryEligibility eligibility = await _composer.ComposeAsync(
                repo.Slug,
                verdict,
                credential,
                token,
                cancellationToken);
            repo.SetEligibility(eligibility);
        }
#pragma warning disable CA1031 // Provider calls may fail with any exception type (network, serialization, etc.) — treat all as unreachable
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(
                ex,
                "Failed to evaluate eligibility for repository {Slug}; marking as unreachable.",
                repo.Slug);
            // Reset verdict to Unknown (with attempt timestamp) so the cheap poll path
            // (EvaluateBranchRulesAndStoreAsync) does not trust a stale Granted verdict from
            // a previous cycle and re-open eligibility to Eligible without a fresh write probe
            // succeeding. Stamping LastAttemptedAt = now ensures the next automatic retry is
            // one cooldown away rather than immediate.
            repo.SetWriteProbeVerdict(new WriteProbeVerdict.Unknown(LastAttemptedAt: now));
            repo.SetEligibility(new RepositoryEligibility.Unreachable());
        }
    }

    /// <inheritdoc/>
    public async Task EvaluateBranchRulesAndStoreAsync(
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

        try
        {
            RepositoryEligibility eligibility = await _composer.ComposeAsync(
                repo.Slug,
                repo.WriteProbeVerdict,
                credential,
                token,
                cancellationToken);
            repo.SetEligibility(eligibility);
        }
#pragma warning disable CA1031 // Provider calls may fail with any exception type (network, serialization, etc.) — treat all as unreachable
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(
                ex,
                "Failed to evaluate eligibility for repository {Slug}; marking as unreachable.",
                repo.Slug);
            repo.SetEligibility(new RepositoryEligibility.Unreachable());
        }
    }

    private async Task<WriteProbeVerdict> RunWriteProbeAsync(
        MonitoredRepository repo,
        Credential credential,
        string token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (credential is GitHubCredential gitHubCredential)
        {
            return await ProbeGitHubWriteAccessAsync(repo, gitHubCredential, token, now, cancellationToken);
        }

        return await ProbeProviderWriteAccessAsync(repo, credential, token, now, cancellationToken);
    }

    private async Task<WriteProbeVerdict> ProbeGitHubWriteAccessAsync(
        MonitoredRepository repo,
        GitHubCredential credential,
        string token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Result<WritePermissionProbeResult> probeResult = await gitHubWriteProber.ProbeWriteAccessAsync(
            credential.ApiBaseUrl,
            repo.Slug,
            token,
            cancellationToken);

        if (probeResult is not Result<WritePermissionProbeResult>.Success { Value: WritePermissionProbeResult probe })
        {
            // Transport failure — stamp the attempt time so the next automatic retry is one cooldown away.
            return new WriteProbeVerdict.Unknown(LastAttemptedAt: now);
        }

        return probe is WritePermissionProbeResult.Missing
            ? new WriteProbeVerdict.Denied()
            : new WriteProbeVerdict.Granted();
    }

    private async Task<WriteProbeVerdict> ProbeProviderWriteAccessAsync(
        MonitoredRepository repo,
        Credential credential,
        string token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IIssueProvider provider = providerFactory.CreateProvider(credential, token);
        Result<bool> canPushResult = await provider.CanPushAsync(repo.Slug, cancellationToken);

        return canPushResult switch
        {
            // Transport failure — stamp the attempt time so the next automatic retry is one cooldown away.
            Result<bool>.Failure => new WriteProbeVerdict.Unknown(LastAttemptedAt: now),
            Result<bool>.Success { Value: false } => new WriteProbeVerdict.Denied(),
            _ => new WriteProbeVerdict.Granted(),
        };
    }
}
