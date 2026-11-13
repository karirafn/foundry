using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.WebhookTests;

/// <summary>
/// Automatic webhook registration with the account's server-side token: the "Register webhook"
/// action, repository add, server-side token save, and a Webhook Public Base URL change.
/// </summary>
public sealed class WhenRegisteringAutomatically : IAsyncDisposable
{
    private const string WorkerToken = "ghp_worker_token";
    private const string ServerToken = "github_pat_server_token";
    private const string PublicBaseUrl = "https://hooks.example.com";

    private readonly ProviderState _state = new();
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRegisteringAutomatically()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.Configure<WebhookIngressOptions>(options => options.Port = WebhookTestHost.WebhookPort);

            services.RemoveAll<IIssueProviderFactory>();
            services.AddSingleton(_state);
            services.AddScoped<IIssueProviderFactory, StatefulProviderFactory>();

            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new ProbeGrantedFakeHandler()),
                    NullLogger<GitHubHttpClient>.Instance,
                    new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
                    new InMemoryProviderRateBudget(),
                    TimeProvider.System));
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<Guid> SeedAccountAsync(string? serverToken)
    {
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(
            _factory,
            name: "Hooks Org",
            token: WorkerToken,
            branchRulesToken: serverToken);
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        return accountId;
    }

    private async Task<RegisterWebhookResponse> RegisterAsync(Guid accountId, MonitoredRepository repository)
    {
        HttpResponseMessage response = await _client.PostAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repository.Id.Value}/webhook/register", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<RegisterWebhookResponse>(
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    [Fact]
    public async Task RegisterAction_CreatesTheHookWithTheServerTokenAndMovesToPending()
    {
        // Arrange
        await WebhookTestHost.SeedSettingsAsync(_factory, PublicBaseUrl);
        Guid accountId = await SeedAccountAsync(ServerToken);
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory);

        // Act
        RegisterWebhookResponse response = await RegisterAsync(accountId, repository);

        // Assert
        response.Outcome.Kind.ShouldBe(WebhookRegistrationOutcome.CreatedKind);
        response.Webhook.Status.ShouldBe("pending");
        (string slug, string token, WebhookHookSpec spec) = _state.Upserts.ShouldHaveSingleItem();
        slug.ShouldBe("owner/repo");
        token.ShouldBe(ServerToken);
        spec.PayloadUrl.ShouldBe($"{PublicBaseUrl}/webhooks/github/{repository.WebhookHookId}");
        spec.Secret.ShouldBe((await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookSecret);
    }

    [Fact]
    public async Task RegisterAction_WithoutServerToken_MakesNoCallAndStaysNotRegistered()
    {
        // Arrange
        await WebhookTestHost.SeedSettingsAsync(_factory, PublicBaseUrl);
        Guid accountId = await SeedAccountAsync(serverToken: null);
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory);

        // Act
        RegisterWebhookResponse response = await RegisterAsync(accountId, repository);

        // Assert
        response.Outcome.ShouldBe(new WebhookRegistrationOutcome(
            WebhookRegistrationOutcome.SkippedKind,
            WebhookRegistrationOutcome.NoServerTokenReason));
        response.Webhook.Status.ShouldBe("not-registered");
        _state.Upserts.ShouldBeEmpty();
    }

    [Fact]
    public async Task RegisterAction_WhenPermissionIsMissing_FailsAndStaysNotRegistered()
    {
        // Arrange
        await WebhookTestHost.SeedSettingsAsync(_factory, PublicBaseUrl);
        Guid accountId = await SeedAccountAsync(ServerToken);
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory);
        _state.UpsertResult = Result<WebhookUpsertKind>.Fail(WebhookWriteErrors.InsufficientPermission("403"));

        // Act
        RegisterWebhookResponse response = await RegisterAsync(accountId, repository);

        // Assert
        response.Outcome.Kind.ShouldBe(WebhookRegistrationOutcome.FailedKind);
        response.Outcome.Reason.ShouldBe(WebhookRegistrationOutcome.InsufficientPermissionReason);
        response.Webhook.Status.ShouldBe("not-registered");
    }

    [Fact]
    public async Task AddingARepository_RegistersItsWebhook()
    {
        // Arrange
        await WebhookTestHost.SeedSettingsAsync(_factory, PublicBaseUrl);
        Guid accountId = await SeedAccountAsync(ServerToken);

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            new { slug = "owner/new-repo", applyBranchRules = false },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        CreateRepositoryResponse created = (await response.Content.ReadFromJsonAsync<CreateRepositoryResponse>(
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
        created.Webhook.ShouldNotBeNull().Kind.ShouldBe(WebhookRegistrationOutcome.CreatedKind);
        created.Repository.WebhookStatus.ShouldBe("pending");
        _state.Upserts.ShouldHaveSingleItem().Token.ShouldBe(ServerToken);
    }

    [Fact]
    public async Task SavingAServerToken_RegistersTheAccountsNotRegisteredRepositories()
    {
        // Arrange
        await WebhookTestHost.SeedSettingsAsync(_factory, PublicBaseUrl);
        Guid accountId = await SeedAccountAsync(serverToken: null);
        MonitoredRepository unregistered = await WebhookTestHost.SeedRepositoryAsync(_factory, slug: "owner/one");
        await WebhookTestHost.SeedRepositoryAsync(_factory, slug: "owner/two", registered: true, position: 1);

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{accountId}", UriKind.Relative),
            new { baseUrl = "https://github.com", token = (string?)null, branchRulesToken = ServerToken },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _state.Upserts.Select(u => (u.Slug, u.Token)).ShouldBe([("owner/one", ServerToken)]);
        (await WebhookTestHost.LoadRepositoryAsync(_factory, unregistered)).WebhookStatus.ShouldBe(WebhookStatus.Pending);
    }

    [Fact]
    public async Task ChangingThePublicBaseUrl_RepointsRegisteredHooksFromTheOldUrl()
    {
        // Arrange
        await WebhookTestHost.SeedSettingsAsync(_factory, "https://old.example.com");
        await SeedAccountAsync(ServerToken);
        MonitoredRepository registered = await WebhookTestHost.SeedRepositoryAsync(_factory, slug: "owner/one", registered: true);
        await WebhookTestHost.SeedRepositoryAsync(_factory, slug: "owner/two", position: 1);

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/webhooks", UriKind.Relative),
            new { webhookPublicBaseUrl = PublicBaseUrl, backstopIntervalMinutes = 15 },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        OutboxRelayService relay = _factory.Services.GetRequiredService<OutboxRelayService>();
        await relay.TickForTest(TestContext.Current.CancellationToken);

        // Assert — only the registered hook is re-pointed, matched against its old payload URL.
        (string slug, string token, WebhookHookSpec spec) = _state.Upserts.ShouldHaveSingleItem();
        slug.ShouldBe("owner/one");
        token.ShouldBe(ServerToken);
        spec.PayloadUrl.ShouldBe($"{PublicBaseUrl}/webhooks/github/{registered.WebhookHookId}");
        spec.PreviousPayloadUrls.ShouldBe([$"https://old.example.com/webhooks/github/{registered.WebhookHookId}"]);
    }

    private sealed class ProviderState
    {
        public Result<WebhookUpsertKind> UpsertResult { get; set; } = Result<WebhookUpsertKind>.Ok(WebhookUpsertKind.Created);

        public ConcurrentQueue<(string Slug, string Token, WebhookHookSpec Spec)> Upserts { get; } = new();
    }

    private sealed class StatefulProviderFactory(ProviderState state) : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) =>
            new StatefulProvider(state, token);
    }

    private sealed class StatefulProvider(ProviderState state, string token) : IIssueProvider
    {
        public Task<Result<WebhookUpsertKind>> UpsertWebhookAsync(
            RepositorySlug slug,
            WebhookHookSpec spec,
            CancellationToken cancellationToken)
        {
            state.Upserts.Enqueue((slug.FullPath, token, spec));
            return Task.FromResult(state.UpsertResult);
        }

        public Task<Result<BranchProtection>> GetBranchProtectionAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<BranchProtection>.Ok(new BranchProtection("main", true, true, true)));

        public Task<Result> ApplyBranchRulesAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok());

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
            Task.FromResult(Result<PullRequestStatus>.Ok(new PullRequestStatus(false, false, null)));

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            DateTimeOffset since,
            PostedCommentIds postedComments,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([], OmittedCommentCount: 0, NewestCommentAt: null)));

        public Task<Result<bool>> CreateBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<bool>> DeleteBranchAsync(
            RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

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
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<Checks>> GetChecksAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<Checks>.Ok(new Checks.NoneDetected()));
    }
}
