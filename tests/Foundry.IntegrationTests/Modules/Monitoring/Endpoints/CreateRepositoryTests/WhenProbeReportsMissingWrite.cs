using System.Net;
using System.Net.Http.Json;

using Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;
using Foundry.IntegrationTests.Modules.Monitoring.Endpoints;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateRepositoryTests;

/// <summary>
/// Verifies the wiring between the write-permission probe and repository eligibility.
/// When the probe returns 403 (Missing) for the repository, eligibility is Ineligible
/// with a cannot-push violation. When the probe returns 422 (Granted) and branch
/// protection is fully configured, eligibility is Eligible.
/// </summary>
public sealed class WhenProbeReportsMissingWrite : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenProbeReportsMissingWrite()
    {
        BranchProtection eligibleProtection = new(
            DefaultBranch: "main",
            RejectDirectPushes: true,
            RejectForcePushes: true,
            RejectDeletion: true);

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IIssueProviderFactory>();
            services.AddScoped<IIssueProviderFactory>(_ =>
                new StubProviderFactory(Result<BranchProtection>.Ok(eligibleProtection)));

            // Probe returns 403 — write access missing; eligibility evaluator marks as Ineligible.
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new ProbeBlockedFakeHandler())));
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenProbeForbidden_ReturnsIneligibleWithCannotPushViolation()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Probe Blocked Org");
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        object body = new { slug = "owner/probe-blocked-repo" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert — 201 is still returned; the eligibility is evaluated and stored, not a 4xx
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        RepositorySummary? repository = await response.Content
            .ReadFromJsonAsync<RepositorySummary>(TestContext.Current.CancellationToken);
        repository.ShouldNotBeNull();
        repository.Eligibility.ShouldNotBeNull();
        repository.Eligibility.ShouldSatisfyAllConditions(
            () => repository.Eligibility.Status.ShouldBe("ineligible"),
            () => repository.Eligibility.Violations.ShouldHaveSingleItem(),
            () => repository.Eligibility.Violations[0].Rule.ShouldBe("cannot-push:owner/probe-blocked-repo"));
    }

    [Fact]
    public async Task WhenProbeGrantedAndBranchProtected_ReturnsEligible()
    {
        // Arrange — use a factory where the probe returns 422 (Granted)
        BranchProtection eligibleProtection = new(
            DefaultBranch: "main",
            RejectDirectPushes: true,
            RejectForcePushes: true,
            RejectDeletion: true);

        FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IIssueProviderFactory>();
            services.AddScoped<IIssueProviderFactory>(_ =>
                new StubProviderFactory(Result<BranchProtection>.Ok(eligibleProtection)));

            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new ProbeGrantedFakeHandler())));
        });

        await using (factory.ConfigureAwait(false))
        {
            using HttpClient client = factory.CreateClient();
            Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(factory, name: "Probe Granted Org");
            await AccountSeeder.SetOwnerNamespacesAsync(factory, accountId, "owner");
            object body = new { slug = "owner/probe-granted-repo" };

            // Act
            HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
                body,
                TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            RepositorySummary? repository = await response.Content
                .ReadFromJsonAsync<RepositorySummary>(TestContext.Current.CancellationToken);
            repository.ShouldNotBeNull();
            repository.Eligibility.ShouldNotBeNull();
            repository.Eligibility.ShouldSatisfyAllConditions(
                () => repository.Eligibility.Status.ShouldBe("eligible"),
                () => repository.Eligibility.Violations.ShouldBeEmpty());
        }
    }

    private sealed class StubProviderFactory(Result<BranchProtection> branchProtectionResult) : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) =>
            new StubProvider(branchProtectionResult);
    }

    private sealed class StubProvider(Result<BranchProtection> branchProtectionResult) : IIssueProvider
    {
        public Task<Result<BranchProtection>> GetBranchProtectionAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken) =>
            Task.FromResult(branchProtectionResult);

        public Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<ProviderIssue>>.Ok([]));

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<int>>.Ok([]));

        public Task<Result<bool>> IsIssueClosedAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<bool>.Ok(false));

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<PullRequestStatus>.Ok(new PullRequestStatus(false, false)));

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            DateTimeOffset since,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([])));

        public Task<Result<bool>> CreateBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<bool>> HasBranchCommitsAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<bool>.Ok(false));

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit found")));

        public Task<Result<bool>> CanPushAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<bool>.Ok(true));
    }
}
