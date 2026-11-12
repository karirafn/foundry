using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared.Infrastructure;
using Foundry.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.WebhookTests;

/// <summary>
/// Push deliveries (issue #660): a push to the branch of a running worker is published as the ephemeral
/// <c>ProviderBranchPushed</c>; any other push is dropped by the relevance filter. Neither enqueues a nudge.
/// </summary>
public sealed class WhenPushDeliveryArrives : IAsyncDisposable
{
    private const string RunBranch = "feat/42-push-tracking";

    private readonly ProviderBranchPushLog _pushes = new();

    private readonly FoundryWebAppFactory _factory;

    public WhenPushDeliveryArrives()
    {
        _factory = WebhookTestHost.CreateFactory(services =>
        {
            services.AddSingleton(_pushes);
            services.AddIntegrationEventHandler<ProviderBranchPushed, ProviderBranchPushRecorder>();
        });
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenGitHubPushTargetsRunningWorkerBranch_PublishesTheBranchPush()
    {
        // Arrange
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, "owner/repo", registered: true);
        await SeedActiveRunAsync(repository.Id, RunBranch);

        // Act
        HttpContext response = await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "push",
            GitHubPushBody("owner/repo", $"refs/heads/{RunBranch}"),
            repository.WebhookSecret);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status202Accepted);
        _pushes.Contains(repository.Id, RunBranch).ShouldBeTrue();
        WebhookTestHost.DrainNudges(_factory).ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenGitHubPushTargetsBranchWithNoRunningWorker_IsDropped()
    {
        // Arrange — the default branch is never a run branch.
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, "owner/repo", registered: true);
        await SeedActiveRunAsync(repository.Id, RunBranch);

        // Act
        HttpContext response = await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "push",
            GitHubPushBody("owner/repo", "refs/heads/main"),
            repository.WebhookSecret);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status202Accepted);
        _pushes.Contains(repository.Id, "main").ShouldBeFalse();
        WebhookTestHost.DrainNudges(_factory).ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenGitHubPushTargetsRunBranchOfAnotherRepository_IsDropped()
    {
        // Arrange
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, "owner/repo", registered: true);
        await SeedActiveRunAsync(MonitoredRepositoryId.New(), RunBranch);

        // Act
        await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "push",
            GitHubPushBody("owner/repo", $"refs/heads/{RunBranch}"),
            repository.WebhookSecret);

        // Assert
        _pushes.Contains(repository.Id, RunBranch).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenGitHubTagPushNamesRunBranch_IsDropped()
    {
        // Arrange
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, "owner/repo", registered: true);
        await SeedActiveRunAsync(repository.Id, RunBranch);

        // Act
        await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "push",
            GitHubPushBody("owner/repo", $"refs/tags/{RunBranch}"),
            repository.WebhookSecret);

        // Assert
        _pushes.Contains(repository.Id, RunBranch).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenGitLabPushHookTargetsRunningWorkerBranch_PublishesTheBranchPush()
    {
        // Arrange
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(
            _factory,
            "group/project",
            registered: true,
            host: "gitlab.com");
        await SeedActiveRunAsync(repository.Id, RunBranch);
        string body = $$$"""
            {"object_kind":"push","ref":"refs/heads/{{{RunBranch}}}","project":{"path_with_namespace":"group/project"}}
            """;

        // Act
        HttpContext response = await WebhookTestHost.DeliverGitLabAsync(
            _factory,
            repository.WebhookHookId,
            "Push Hook",
            body,
            repository.WebhookSecret);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status202Accepted);
        _pushes.Contains(repository.Id, RunBranch).ShouldBeTrue();
        WebhookTestHost.DrainNudges(_factory).ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenHookActiveAndIngressConfigured_WebhookStatusQueryReportsActive()
    {
        // Arrange — a verified delivery activates the hook; ingress needs the port and a public base URL.
        await WebhookTestHost.SeedSettingsAsync(_factory, "https://foundry.example.com");
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, "owner/repo", registered: true);
        await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "ping",
            """{"zen":"hi","repository":{"full_name":"owner/repo"}}""",
            repository.WebhookSecret);

        // Act
        bool active = await IsWebhookActiveAsync(repository.Id);

        // Assert
        active.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenHookActiveButIngressNotConfigured_WebhookStatusQueryReportsNotActive()
    {
        // Arrange — no public base URL: the effective status is not-configured.
        await WebhookTestHost.SeedSettingsAsync(_factory, publicBaseUrl: null);
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, "owner/repo", registered: true);
        await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "ping",
            """{"zen":"hi","repository":{"full_name":"owner/repo"}}""",
            repository.WebhookSecret);

        // Act
        bool active = await IsWebhookActiveAsync(repository.Id);

        // Assert
        active.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenHookPending_WebhookStatusQueryReportsNotActive()
    {
        // Arrange
        await WebhookTestHost.SeedSettingsAsync(_factory, "https://foundry.example.com");
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, "owner/repo", registered: true);

        // Act
        bool active = await IsWebhookActiveAsync(repository.Id);

        // Assert
        active.ShouldBeFalse();
    }

    private static string GitHubPushBody(string slug, string pushedRef) => $$$"""
        {"ref":"{{{pushedRef}}}","before":"0000","after":"1111","repository":{"full_name":"{{{slug}}}"}}
        """;

    private async Task<bool> IsWebhookActiveAsync(MonitoredRepositoryId repositoryId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IWebhookStatusQueries queries = scope.ServiceProvider.GetRequiredService<IWebhookStatusQueries>();
        return await queries.IsWebhookActiveAsync(repositoryId, TestContext.Current.CancellationToken);
    }

    private async Task SeedActiveRunAsync(MonitoredRepositoryId repositoryId, string branch)
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        ActiveRun run = starting.Activate(ContainerId.From("container-push"), BranchName.From(branch), repositoryId);

        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        dbContext.Set<WorkerRun>().Add(run);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
