using System.Net;
using System.Net.Http.Json;
using System.Text;

using Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.RecheckRepositoryEligibilityTests;

public sealed class WhenTokenRegainsPushAccess : IAsyncDisposable
{
    private readonly ConfigurableProbeHandler _probeHandler;
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTokenRegainsPushAccess()
    {
        // Start with probes blocked (403) — eligibility evaluator sees Missing and marks Ineligible.
        _probeHandler = new ConfigurableProbeHandler(probeGranted: false);

        BranchProtection eligibleProtection = new(
            DefaultBranch: "main",
            RejectDirectPushes: true,
            RejectForcePushes: true,
            RejectDeletion: true);

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            // Provide eligible branch protection so the test can reach Eligible after probe is granted.
            services.RemoveAll<IIssueProviderFactory>();
            services.AddScoped<IIssueProviderFactory>(_ =>
                new StubProviderFactory(Result<BranchProtection>.Ok(eligibleProtection)));

            // Use a configurable probe handler; state is toggled between initial seeding and recheck.
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(new HttpClient(_probeHandler), NullLogger<GitHubHttpClient>.Instance));
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        _probeHandler.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsEligibleAfterRecheck()
    {
        // Arrange — seed repo while probe returns 403, leaving it ineligible (cannot-push)
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Regained Push Org");
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(
            _factory, accountId, slug: "owner/regained-push-repo");

        // Flip the probe handler so recheck now grants write access.
        _probeHandler.ProbeGranted = true;

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repositoryId}/recheck", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        RepositorySummary? repository = await response.Content
            .ReadFromJsonAsync<RepositorySummary>(TestContext.Current.CancellationToken);
        repository.ShouldNotBeNull();
        repository.Eligibility.ShouldNotBeNull();
        repository.Eligibility.ShouldSatisfyAllConditions(
            () => repository.Eligibility.Status.ShouldBe("eligible"),
            () => repository.Eligibility.Violations.ShouldBeEmpty());
    }

    /// <summary>
    /// Returns 403 or 422 for probe POSTs based on the current ProbeGranted state.
    /// Non-probe requests receive an empty listing.
    /// </summary>
    private sealed class ConfigurableProbeHandler(bool probeGranted) : DelegatingHandler
    {
        private const string EmptyListingJson = "[]";

        public bool ProbeGranted { get; set; } = probeGranted;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (StaticListingFakeHandler.IsProbePost(request))
            {
                HttpStatusCode statusCode = ProbeGranted
                    ? HttpStatusCode.UnprocessableEntity
                    : HttpStatusCode.Forbidden;
                return Task.FromResult(new HttpResponseMessage(statusCode));
            }

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(EmptyListingJson, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
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
