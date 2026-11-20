using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;

using Foundry.IntegrationTests.Shared;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Webhooks.Ingress;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.WebhookTests;

/// <summary>
/// Automatic webhook registration with the account token: the "Register webhook" action,
/// repository add, a Webhook Public Base URL change, and hook deletion on
/// repository removal.
/// </summary>
public sealed class WhenRegisteringAutomatically : IAsyncDisposable
{
    private const string AccountToken = "ghp_account_token";
    private const string PublicBaseUrl = "https://hooks.example.com";
    private const long CreatedProviderHookId = 4242;

    private readonly ProviderState _state = new();
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRegisteringAutomatically()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.Configure<WebhookIngressOptions>(options => options.Port = WebhookTestHost.WebhookPort);
            services.AddSingleton<IWebhookIngressProbe>(new StubWebhookIngressProbe(WebhookTestHost.WebhookPort, new WebhookProbeResult.Reached()));

            services.RemoveAll<IIssueProviderFactory>();
            services.AddSingleton(_state);
            services.AddScoped<IIssueProviderFactory, StatefulProviderFactory>();
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<Guid> SeedAccountAsync(string? accountToken = AccountToken)
    {
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(
            _factory,
            name: "Hooks Org",
            token: accountToken);
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
    public async Task RegisterAction_CreatesTheHookWithTheAccountTokenAndMovesToPending()
    {
        // Arrange
        await WebhookTestHost.SeedSettingsAsync(_factory, PublicBaseUrl);
        Guid accountId = await SeedAccountAsync();
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory);

        // Act
        RegisterWebhookResponse response = await RegisterAsync(accountId, repository);

        // Assert
        response.Outcome.Kind.ShouldBe(WebhookRegistrationOutcome.CreatedKind);
        response.Webhook.Status.ShouldBe("pending");
        (string slug, string token, WebhookHookSpec spec) = _state.Upserts.ShouldHaveSingleItem();
        slug.ShouldBe("owner/repo");
        token.ShouldBe(AccountToken);
        spec.PayloadUrl.ShouldBe($"{PublicBaseUrl}/webhooks/github/{repository.WebhookHookId}");
        spec.Secret.ShouldBe((await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookSecret);
        _state.Tests.ShouldBe([("owner/repo", AccountToken, CreatedProviderHookId)]);
    }

    [Fact]
    public async Task RegisterAction_WithoutAccountToken_MakesNoCallAndStaysNotRegistered()
    {
        // Arrange
        await WebhookTestHost.SeedSettingsAsync(_factory, PublicBaseUrl);
        Guid accountId = await SeedAccountAsync(accountToken: null);
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory);

        // Act
        RegisterWebhookResponse response = await RegisterAsync(accountId, repository);

        // Assert
        response.Outcome.ShouldBe(new WebhookRegistrationOutcome(
            WebhookRegistrationOutcome.SkippedKind,
            WebhookRegistrationOutcome.TokenUnreadableReason));
        response.Webhook.Status.ShouldBe("not-registered");
        _state.Upserts.ShouldBeEmpty();
    }

    [Fact]
    public async Task RegisterAction_WhenPermissionIsMissing_FailsAndStaysNotRegistered()
    {
        // Arrange
        await WebhookTestHost.SeedSettingsAsync(_factory, PublicBaseUrl);
        Guid accountId = await SeedAccountAsync();
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory);
        _state.UpsertResult = Result<WebhookUpsert>.Fail(WebhookWriteErrors.InsufficientPermission("403"));

        // Act
        RegisterWebhookResponse response = await RegisterAsync(accountId, repository);

        // Assert
        response.Outcome.Kind.ShouldBe(WebhookRegistrationOutcome.FailedKind);
        response.Outcome.Reason.ShouldBe(WebhookRegistrationOutcome.InsufficientPermissionReason);
        response.Webhook.Status.ShouldBe("not-registered");
        _state.Tests.ShouldBeEmpty();
    }

    [Fact]
    public async Task RegisterAction_ForGitLab_TheTestPushDeliveryActivatesTheHook()
    {
        // Arrange
        await WebhookTestHost.SeedSettingsAsync(_factory, PublicBaseUrl);
        Guid accountId = await AccountSeeder.SeedGitLabAccountAsync(_factory, name: "Hooks Group");
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "group");
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(
            _factory, slug: "group/project", host: "gitlab.com");
        RegisterWebhookResponse registered = await RegisterAsync(accountId, repository);
        registered.Webhook.Status.ShouldBe("pending");
        _state.Tests.ShouldBe([("group/project", "glpat-test_token", CreatedProviderHookId)]);
        string secret = (await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookSecret!;

        // Act — the shape of GitLab's sample push event, as the existing GitLab delivery tests send it.
        HttpContext delivery = await WebhookTestHost.DeliverGitLabAsync(
            _factory,
            repository.WebhookHookId,
            "Push Hook",
            """{"object_kind":"push","ref":"refs/heads/main","project":{"path_with_namespace":"group/project"}}""",
            secret);

        // Assert
        delivery.Response.StatusCode.ShouldBe(StatusCodes.Status202Accepted);
        (await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookStatus.ShouldBe(WebhookStatus.Active);
    }

    [Fact]
    public async Task RegisterAction_WhenUpdatedInPlace_PingsTheHookAndThePingActivatesIt()
    {
        // Arrange — a hook left at an older base URL is updated in place.
        await WebhookTestHost.SeedSettingsAsync(_factory, PublicBaseUrl);
        Guid accountId = await SeedAccountAsync();
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, registered: true);
        _state.UpsertResult = Result<WebhookUpsert>.Ok(new WebhookUpsert(WebhookUpsertKind.Updated, 77));
        RegisterWebhookResponse registered = await RegisterAsync(accountId, repository);
        registered.Outcome.Kind.ShouldBe(WebhookRegistrationOutcome.UpdatedKind);
        _state.Tests.ShouldBe([("owner/repo", AccountToken, 77L)]);
        string secret = (await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookSecret!;

        // Act
        HttpContext delivery = await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "ping",
            """{"zen":"x","repository":{"full_name":"owner/repo"}}""",
            secret);

        // Assert
        delivery.Response.StatusCode.ShouldBe(StatusCodes.Status202Accepted);
        (await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookStatus.ShouldBe(WebhookStatus.Active);
    }

    [Fact]
    public async Task AddingARepository_RegistersItsWebhook()
    {
        // Arrange
        await WebhookTestHost.SeedSettingsAsync(_factory, PublicBaseUrl);
        Guid accountId = await SeedAccountAsync();

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
        _state.Upserts.ShouldHaveSingleItem().Token.ShouldBe(AccountToken);
    }

    [Fact]
    public async Task ChangingThePublicBaseUrl_RegistersEveryRepositoryMatchedByHookId()
    {
        // Arrange
        await WebhookTestHost.SeedSettingsAsync(_factory, "https://old.example.com");
        await SeedAccountAsync();
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

        // Assert — every repository is registered, NotRegistered included, each matched by its hook id.
        _state.Upserts.Select(u => u.Slug).ShouldBe(["owner/one", "owner/two"]);
        _state.Upserts.ShouldAllBe(u => u.Token == AccountToken);
        WebhookHookSpec spec = _state.Upserts.First().Spec;
        spec.PayloadUrl.ShouldBe($"{PublicBaseUrl}/webhooks/github/{registered.WebhookHookId}");
        spec.Matches($"https://old.example.com/webhooks/github/{registered.WebhookHookId}").ShouldBeTrue();
    }

    [Fact]
    public async Task RemovingARepository_DeletesItsHooksWithTheAccountToken()
    {
        // Arrange
        await WebhookTestHost.SeedSettingsAsync(_factory, PublicBaseUrl);
        Guid accountId = await SeedAccountAsync();
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, registered: true);

        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repository.Id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        _state.Deletes.ShouldHaveSingleItem().ShouldBe(("owner/repo", AccountToken, repository.WebhookHookId));
    }

    [Fact]
    public async Task RemovingARepository_WhenTheHookDeletionFails_StillRemovesIt()
    {
        // Arrange
        await WebhookTestHost.SeedSettingsAsync(_factory, PublicBaseUrl);
        Guid accountId = await SeedAccountAsync();
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, registered: true);
        _state.DeleteResult = Result<int>.Fail(WebhookWriteErrors.InsufficientPermission("403"));

        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repository.Id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        HttpResponseMessage again = await _client.DeleteAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repository.Id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private sealed class ProviderState
    {
        public Result<WebhookUpsert> UpsertResult { get; set; } = Result<WebhookUpsert>.Ok(new WebhookUpsert(WebhookUpsertKind.Created, CreatedProviderHookId));

        public ConcurrentQueue<(string Slug, string Token, long ProviderHookId)> Tests { get; } = new();

        public ConcurrentQueue<(string Slug, string Token, WebhookHookSpec Spec)> Upserts { get; } = new();

        public Result<int> DeleteResult { get; set; } = Result<int>.Ok(1);

        public ConcurrentQueue<(string Slug, string Token, string HookId)> Deletes { get; } = new();
    }

    private sealed class StatefulProviderFactory(ProviderState state) : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) =>
            new StatefulProvider(state, token);
    }

    private sealed class StatefulProvider(ProviderState state, string token) : IIssueProvider
    {
        public Task<Result<WebhookUpsert>> UpsertWebhookAsync(
            RepositorySlug slug,
            WebhookHookSpec spec,
            CancellationToken cancellationToken)
        {
            state.Upserts.Enqueue((slug.FullPath, token, spec));
            return Task.FromResult(state.UpsertResult);
        }

        public Task<Result> TestWebhookAsync(
            RepositorySlug slug,
            long providerHookId,
            CancellationToken cancellationToken)
        {
            state.Tests.Enqueue((slug.FullPath, token, providerHookId));
            return Task.FromResult(Result.Ok());
        }

        public Task<Result<int>> DeleteWebhooksAsync(
            RepositorySlug slug,
            string hookId,
            CancellationToken cancellationToken)
        {
            state.Deletes.Enqueue((slug.FullPath, token, hookId));
            return Task.FromResult(state.DeleteResult);
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
            CommitSha? publishedHead,
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
