using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;

using Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;
using Foundry.IntegrationTests.Modules.Monitoring.Endpoints;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateRepositoryTests;

public sealed class WhenAccountHasToken : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountHasToken()
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

            // Probe-aware: probe POSTs return 422 (Granted) so eligibility evaluator passes
            // the write-permission check and proceeds to branch-protection evaluation.
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new ProbeGrantedFakeHandler()),
                    NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System));
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsEligibilityInSummary()
    {
        // Arrange — set namespace so resolver can cover the repo
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Eligible Org");
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        object body = new
        {
            slug = "owner/protected-repo",
            pollIntervalSeconds = 300,
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
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

    [Fact]
    public async Task WhenProtectionHasViolations_ReturnsIneligibleStatus()
    {
        // Arrange
        BranchProtection ineligibleProtection = new(
            DefaultBranch: "main",
            RejectDirectPushes: false,
            RejectForcePushes: true,
            RejectDeletion: true);

        FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IIssueProviderFactory>();
            services.AddScoped<IIssueProviderFactory>(_ =>
                new StubProviderFactory(Result<BranchProtection>.Ok(ineligibleProtection)));

            // Probe-aware: probe POSTs return 422 (Granted) so the evaluator proceeds to
            // branch-protection evaluation, which then surfaces the violation.
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new ProbeGrantedFakeHandler()),
                    NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System));
        });
        await using (factory.ConfigureAwait(false))
        {
            using HttpClient client = factory.CreateClient();
            Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(factory, name: "Ineligible Org");
            await AccountSeeder.SetOwnerNamespacesAsync(factory, accountId, "owner");
            object body = new
            {
                slug = "owner/unprotected-repo",
            };

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
                () => repository.Eligibility.Status.ShouldBe("ineligible"),
                () => repository.Eligibility.Violations.ShouldHaveSingleItem());
        }
    }

    [Fact]
    public async Task WhenNoNamespaceCoversRepo_EligibilityIsIneligibleWithNoCredentialViolation()
    {
        // Arrange — account has a token but no namespaces configured, so the resolver returns null
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "No Namespace Org");
        object body = new
        {
            slug = "owner/no-namespace-repo",
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
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
            () => repository.Eligibility.Status.ShouldBe("ineligible"),
            () => repository.Eligibility.Violations.ShouldHaveSingleItem(),
            () => repository.Eligibility.Violations[0].Rule.ShouldBe("no-credential:owner"));
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

        public Task<Result<IssueListing>> GetIssuesAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<IssueListing>.Ok(new IssueListing([], IsComplete: true)));

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
            Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([], OmittedCommentCount: 0, NewestCommentAt: null)));

        public Task<Result<bool>> CreateBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Result<BranchCommitSummary>.Fail(new Error("Provider.NoCommit", "No commit found")));

        public Task<Result<bool>> CanPushAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));
    }

}
