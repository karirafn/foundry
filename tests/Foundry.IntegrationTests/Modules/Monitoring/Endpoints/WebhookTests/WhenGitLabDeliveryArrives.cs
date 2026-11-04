using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Webhooks;

using Microsoft.AspNetCore.Http;

using Shouldly;

using Xunit;

using WebhookStatus = Foundry.Modules.Monitoring.Domain.ValueObjects.WebhookStatus;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.WebhookTests;

/// <summary>Verification at the boundary, health activation and nudge enqueueing for GitLab deliveries.</summary>
public sealed class WhenGitLabDeliveryArrives : IAsyncDisposable
{
    private const string Slug = "group/sub/project";

    private const string IssueBody = """
        {"object_kind":"issue","event_type":"issue","project":{"path_with_namespace":"group/sub/project"},
         "object_attributes":{"iid":12,"action":"open"},"labels":[{"title":"foundry"}]}
        """;

    private readonly FoundryWebAppFactory _factory = WebhookTestHost.CreateFactory();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private Task<MonitoredRepository> SeedAsync(bool registered = true) =>
        WebhookTestHost.SeedRepositoryAsync(_factory, Slug, registered, host: "gitlab.com");

    [Fact]
    public async Task WhenHookIdUnknown_Returns404()
    {
        // Arrange
        await SeedAsync();

        // Act
        HttpContext response = await WebhookTestHost.DeliverGitLabAsync(
            _factory,
            new string('a', MonitoredRepository.WebhookHookIdLength),
            "Issue Hook",
            IssueBody,
            "any-token");

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task WhenTokenMissing_Returns401()
    {
        // Arrange
        MonitoredRepository repository = await SeedAsync();

        // Act
        HttpContext response = await WebhookTestHost.DeliverGitLabAsync(
            _factory,
            repository.WebhookHookId,
            "Issue Hook",
            IssueBody,
            token: null);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        (await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookStatus.ShouldBe(WebhookStatus.Pending);
    }

    [Fact]
    public async Task WhenTokenWrong_Returns401WithoutParsingBody()
    {
        // Arrange — the body is not even JSON: a 401 proves verification ran before parsing.
        MonitoredRepository repository = await SeedAsync();

        // Act
        HttpContext response = await WebhookTestHost.DeliverGitLabAsync(
            _factory,
            repository.WebhookHookId,
            "Issue Hook",
            "not json {",
            "wrong-token");

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        WebhookTestHost.DrainNudges(_factory).ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenGitHubSignatureSentToGitLabEndpoint_Returns401()
    {
        // Arrange — a GitHub-style signed delivery carries no X-Gitlab-Token.
        MonitoredRepository repository = await SeedAsync();

        // Act
        HttpContext response = await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "issues",
            IssueBody,
            repository.WebhookSecret,
            pathPrefix: "/webhooks/gitlab/");

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task WhenBodyNamesAnotherProject_Returns400AndEnqueuesNothing()
    {
        // Arrange
        MonitoredRepository repository = await SeedAsync();
        string body = IssueBody.Replace("group/sub/project", "group/project", StringComparison.Ordinal);

        // Act
        HttpContext response = await WebhookTestHost.DeliverGitLabAsync(
            _factory,
            repository.WebhookHookId,
            "Issue Hook",
            body,
            repository.WebhookSecret);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        WebhookTestHost.DrainNudges(_factory).ShouldBeEmpty();
        (await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookStatus.ShouldBe(WebhookStatus.Pending);
    }

    [Fact]
    public async Task WhenVerifiedRelevantIssueEvent_Returns202ActivatesAndEnqueuesOneListingNudge()
    {
        // Arrange — the nested group path matches case-insensitively.
        MonitoredRepository repository = await SeedAsync();
        string body = IssueBody.Replace("group/sub/project", "Group/Sub/Project", StringComparison.Ordinal);

        // Act
        HttpContext response = await WebhookTestHost.DeliverGitLabAsync(
            _factory,
            repository.WebhookHookId,
            "Issue Hook",
            body,
            repository.WebhookSecret);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status202Accepted);
        (await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookStatus.ShouldBe(WebhookStatus.Active);
        WebhookTestHost.DrainNudges(_factory).ShouldBe([new Nudge(new NudgeKey(repository.Id, NudgePass.IssueListing))]);
    }

    [Fact]
    public async Task WhenVerifiedTestDeliveryIsIrrelevant_ActivatesHookWithoutNudge()
    {
        // Arrange — GitLab has no ping: the "Test" action sends a sample event, which activates the hook.
        MonitoredRepository repository = await SeedAsync();
        string body = """{"object_kind":"push","ref":"refs/heads/main","project":{"path_with_namespace":"group/sub/project"}}""";

        // Act
        HttpContext response = await WebhookTestHost.DeliverGitLabAsync(
            _factory,
            repository.WebhookHookId,
            "Push Hook",
            body,
            repository.WebhookSecret);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status202Accepted);
        (await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookStatus.ShouldBe(WebhookStatus.Active);
        WebhookTestHost.DrainNudges(_factory).ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenDeliveredOnMainPort_Returns404()
    {
        // Arrange
        MonitoredRepository repository = await SeedAsync();

        // Act
        HttpContext response = await WebhookTestHost.DeliverGitLabAsync(
            _factory,
            repository.WebhookHookId,
            "Issue Hook",
            IssueBody,
            repository.WebhookSecret,
            WebhookTestHost.MainPort);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        (await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookStatus.ShouldBe(WebhookStatus.Pending);
    }
}
