using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.RecheckRepositoryEligibilityTests;

public sealed class WhenTokenRegainsPushAccess : IAsyncDisposable
{
    private readonly ConfigurableStubProviderFactory _providerFactory;
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTokenRegainsPushAccess()
    {
        _providerFactory = new ConfigurableStubProviderFactory(canPush: false);

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IIssueProviderFactory>();
            services.AddScoped<IIssueProviderFactory>(_ => _providerFactory);
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsEligibleAfterRecheck()
    {
        // Arrange — seed repo while CanPushAsync returns false, leaving it ineligible
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Regained Push Org");
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(
            _factory, accountId, slug: "owner/regained-push-repo");

        // flip the stub so recheck now reports push access as granted
        _providerFactory.CanPush = true;

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

    private sealed class ConfigurableStubProviderFactory(bool canPush) : IIssueProviderFactory
    {
        public bool CanPush { get; set; } = canPush;

        public IIssueProvider CreateProvider(Credential credential, string token) =>
            new ConfigurableStubProvider(this);
    }

    private sealed class ConfigurableStubProvider(ConfigurableStubProviderFactory factory) : IIssueProvider
    {
        public Task<Result<BranchProtection>> GetBranchProtectionAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<BranchProtection>.Ok(new BranchProtection(
                DefaultBranch: "main",
                RejectDirectPushes: true,
                RejectForcePushes: true,
                RejectDeletion: true)));

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
            Task.FromResult(Result<bool>.Ok(factory.CanPush));
    }
}
