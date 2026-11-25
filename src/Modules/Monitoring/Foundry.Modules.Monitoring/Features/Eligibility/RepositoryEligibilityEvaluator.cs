using System.Diagnostics;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.PackageSources;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Eligibility;

internal sealed class RepositoryEligibilityEvaluator(
    ICredentialResolver credentialResolver,
    IIssueProviderFactory providerFactory,
    IGitHubWriteProber gitHubWriteProber,
    IGitLabTokenGrantReader gitLabGrantReader,
    IPackageSourceVerdicts packageSourceVerdicts,
    ILogger<RepositoryEligibilityEvaluator> logger) : IRepositoryEligibilityEvaluator
{
    private readonly EligibilityComposer _composer = new(providerFactory);

    /// <summary>
    /// Stores the no-credential ineligibility a repository no account's Namespace Claim covers carries, without any
    /// provider call. Returns whether the stored eligibility changed, so a caller saves only on a change.
    /// </summary>
    internal static bool StoreNoCredential(MonitoredRepository repo)
    {
        EligibilityViolation violation = NoCredentialViolation(repo.Slug);

        if (repo.Eligibility is RepositoryEligibility.Ineligible { Violations: [EligibilityViolation stored] }
            && stored == violation)
        {
            return false;
        }

        repo.SetEligibility(new RepositoryEligibility.Ineligible([violation]));
        return true;
    }

    // PrefixesOf is longest-first, so the last prefix is the top-level namespace an account would need to claim.
    private static EligibilityViolation NoCredentialViolation(RepositorySlug slug) =>
        EligibilityViolation.NoCredential(Namespace.PrefixesOf(slug)[^1].Value);

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
            StoreNoCredential(repo);
            return;
        }

        TokenUsability usability = credential.TokenUsability;
        string token;
        switch (usability)
        {
            case TokenUsability.Usable usable:
                token = usable.Token;
                break;
            case TokenUsability.Unreadable:
                repo.SetEligibility(new RepositoryEligibility.Ineligible(
                    [EligibilityViolation.CredentialUnreadable()]));
                return;
            case TokenUsability.Invalid:
                repo.SetEligibility(new RepositoryEligibility.Ineligible(
                    [EligibilityViolation.CredentialInvalid()]));
                return;
            case TokenUsability.Absent:
                string topLevelNamespace = Namespace.PrefixesOf(repo.Slug)[^1].Value;
                repo.SetEligibility(new RepositoryEligibility.Ineligible(
                    [EligibilityViolation.NoCredential(topLevelNamespace)]));
                return;
            default:
                throw new UnreachableException($"Unhandled TokenUsability variant: {usability.GetType().Name}");
        }

        string permissionListVersion = TokenPermissions.VersionFor(ProviderTypeOf(credential));
        RepositoryEligibility eligibility;
        try
        {
            WriteProbeVerdict verdict = await RunWriteProbeAsync(repo, credential, token, now, cancellationToken)
                with { PermissionListVersion = permissionListVersion };
            repo.SetWriteProbeVerdict(verdict);

            eligibility = await _composer.ComposeAsync(
                repo.Slug,
                verdict,
                credential,
                token,
                cancellationToken);
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
            repo.SetWriteProbeVerdict(new WriteProbeVerdict.Unknown(LastAttemptedAt: now) { PermissionListVersion = permissionListVersion });
            eligibility = new RepositoryEligibility.Unreachable(UnreachableReason.NeverProbed);
        }

        repo.SetEligibility(await WithPackageSourceViolationsAsync(repo, credential, eligibility, cancellationToken));
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
            StoreNoCredential(repo);
            return;
        }

        TokenUsability usability = credential.TokenUsability;
        string token;
        switch (usability)
        {
            case TokenUsability.Usable usable:
                token = usable.Token;
                break;
            case TokenUsability.Unreadable:
                repo.SetEligibility(new RepositoryEligibility.Ineligible(
                    [EligibilityViolation.CredentialUnreadable()]));
                return;
            case TokenUsability.Invalid:
                repo.SetEligibility(new RepositoryEligibility.Ineligible(
                    [EligibilityViolation.CredentialInvalid()]));
                return;
            case TokenUsability.Absent:
                string topLevelNamespace = Namespace.PrefixesOf(repo.Slug)[^1].Value;
                repo.SetEligibility(new RepositoryEligibility.Ineligible(
                    [EligibilityViolation.NoCredential(topLevelNamespace)]));
                return;
            default:
                throw new UnreachableException($"Unhandled TokenUsability variant: {usability.GetType().Name}");
        }

        RepositoryEligibility eligibility;
        try
        {
            eligibility = await _composer.ComposeAsync(
                repo.Slug,
                repo.WriteProbeVerdict,
                credential,
                token,
                cancellationToken);
        }
#pragma warning disable CA1031 // Provider calls may fail with any exception type (network, serialization, etc.) — treat all as unreachable
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(
                ex,
                "Failed to evaluate eligibility for repository {Slug}; marking as unreachable.",
                repo.Slug);
            eligibility = new RepositoryEligibility.Unreachable(UnreachableReason.NeverProbed);
        }

        repo.SetEligibility(await WithPackageSourceViolationsAsync(repo, credential, eligibility, cancellationToken));
    }

    /// <summary>
    /// Adds a violation for each declared Package Source whose stored verdict, for the credential Foundry reads it
    /// with, is not Readable (Unchecked included), and for a root <c>nuget.config</c> recorded as invalid. They join
    /// any other violations in one Ineligible list, and replace Eligible and Unreachable: an unreadable source is a
    /// concrete cause the operator can fix, and every one of these states blocks dispatch.
    /// </summary>
    private async Task<RepositoryEligibility> WithPackageSourceViolationsAsync(
        MonitoredRepository repo,
        Credential account,
        RepositoryEligibility composed,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<EligibilityViolation> violations = await PackageSourceViolationsAsync(repo, account, cancellationToken);

        if (violations.Count == 0)
        {
            return composed;
        }

        return composed is RepositoryEligibility.Ineligible ineligible
            ? new RepositoryEligibility.Ineligible([.. ineligible.Violations, .. violations])
            : new RepositoryEligibility.Ineligible(violations);
    }

    private async Task<IReadOnlyList<EligibilityViolation>> PackageSourceViolationsAsync(
        MonitoredRepository repo,
        Credential account,
        CancellationToken cancellationToken)
    {
        if (repo.RootNuGetConfig is RootNuGetConfig.Invalid)
        {
            return [EligibilityViolation.NuGetConfigInvalid()];
        }

        List<EligibilityViolation> violations = [];

        foreach (DeclaredPackageSource source in repo.DeclaredPackageSources)
        {
            ProbeCredential fallback = PackageSourceAccess.FallbackFor(
                source.ServiceIndexUrl,
                repo.Host,
                account);
            PackageSourceVerdict verdict = await packageSourceVerdicts.VerdictForAsync(
                source.ServiceIndexUrl,
                fallback,
                cancellationToken);

            EligibilityViolation violation = EligibilityViolation.PackageSourceUnreachable(source.ServiceIndexUrl);
            if (verdict is not PackageSourceVerdict.Readable && !violations.Contains(violation))
            {
                violations.Add(violation);
            }
        }

        return violations;
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

        if (credential is GitLabCredential gitLabCredential)
        {
            WriteProbeVerdict? grantVerdict = await CheckGitLabGrantAsync(repo, gitLabCredential, token, now, cancellationToken);
            if (grantVerdict is not null)
            {
                return grantVerdict;
            }
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
        // A token of the wrong kind is refused whatever it holds, so no probe is sent. The account summary
        // reads the reason from the token itself, which is why the verdict lists no missing permissions.
        if (GitHubTokenKind.WrongKindOf(token) is not null)
        {
            return new WriteProbeVerdict.Denied(ProbedAt: now) { MissingPermissions = [] };
        }

        Result<WritePermissionProbeResult> probeResult = await gitHubWriteProber.ProbeWriteAccessAsync(
            credential.ApiBaseUrl,
            repo.Slug,
            token,
            cancellationToken);

        if (probeResult is not Result<WritePermissionProbeResult>.Success { Value: WritePermissionProbeResult probe })
        {
            // Stamp the attempt time so the next automatic retry is one cooldown away.
            // Distinguish rate-limit exhaustion from other transport failures so the eligibility
            // composer can surface the real cause (Unreachable vs. a transient network issue).
            UnknownReason reason = probeResult is Result<WritePermissionProbeResult>.Failure failure
                && failure.Error.Code == GitHubErrors.RateLimitExhausted.Code
                    ? UnknownReason.RateLimited
                    : UnknownReason.Transport;

            return new WriteProbeVerdict.Unknown(LastAttemptedAt: now, Reason: reason);
        }

        return probe is WritePermissionProbeResult.Missing missing
            ? new WriteProbeVerdict.Denied(ProbedAt: now)
            {
                MissingPermissions = TokenPermissions.GitHubMissingKeys(missing.Permissions),
            }
            : new WriteProbeVerdict.Granted();
    }

    /// <summary>
    /// Checks the GitLab token's grant before its role: a token Foundry refuses as a whole, or one whose
    /// grant targets no project entry at this repository, is <c>Denied</c>. Returns <see langword="null"/>
    /// when the grant conforms and covers the repository, so the Maintainer role check runs next.
    /// </summary>
    private async Task<WriteProbeVerdict?> CheckGitLabGrantAsync(
        MonitoredRepository repo,
        GitLabCredential credential,
        string token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Result<GitLabTokenSelf> selfResult = await gitLabGrantReader.ReadTokenSelfAsync(
            credential.ApiBaseUrl,
            token,
            cancellationToken);

        if (selfResult is not Result<GitLabTokenSelf>.Success { Value: GitLabTokenSelf self })
        {
            return new WriteProbeVerdict.Unknown(LastAttemptedAt: now);
        }

        switch (self)
        {
            case GitLabTokenSelf.OfWrongKind wrongKind:
                return new WriteProbeVerdict.Denied(ProbedAt: now) { WrongKind = wrongKind.Kind.WireName() };
            case GitLabTokenSelf.CannotReadItself:
                return new WriteProbeVerdict.Denied(ProbedAt: now)
                {
                    MissingPermissions = [TokenPermissions.GitLabReadOwnTokenKey],
                };
        }

        GitLabTokenGrant grant = ((GitLabTokenSelf.FineGrained)self).Grant;
        if (grant.Evaluate(TokenPermissions.GitLabGrantRequirements) is GitLabGrantEvaluation.NonConforming evaluation)
        {
            return new WriteProbeVerdict.Denied(ProbedAt: now)
            {
                MissingPermissions = evaluation.Missing,
                NotAllowed = evaluation.NotAllowed,
                BroadReach = evaluation.BroadReach,
            };
        }

        Result<int> projectId = await gitLabGrantReader.ResolveProjectIdAsync(
            credential.ApiBaseUrl,
            repo.Slug,
            token,
            cancellationToken);

        return projectId switch
        {
            Result<int>.Success { Value: int id } when grant.Covers(id) => null,
            Result<int>.Success => NotSelected(now),
            // A project the token does not select is hidden from it, so GitLab answers 404 or 403.
            Result<int>.Failure failure
                when GitLabErrors.TryGetStatusCode(failure.Error, out int status) && status is 403 or 404 => NotSelected(now),
            _ => new WriteProbeVerdict.Unknown(LastAttemptedAt: now),
        };
    }

    private static WriteProbeVerdict.Denied NotSelected(DateTimeOffset now) =>
        new(ProbedAt: now) { MissingPermissions = [TokenPermissions.GitLabSelectedProjectKey] };

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
            Result<bool>.Success { Value: false } => new WriteProbeVerdict.Denied(ProbedAt: now)
            {
                MissingPermissions = [TokenPermissions.GitLabRoleKey],
            },
            _ => new WriteProbeVerdict.Granted(),
        };
    }

    private static string ProviderTypeOf(Credential credential) =>
        credential is GitLabCredential ? ProviderTypes.GitLab : ProviderTypes.GitHub;
}
