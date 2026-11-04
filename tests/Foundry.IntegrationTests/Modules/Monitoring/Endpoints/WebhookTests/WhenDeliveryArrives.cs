using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Webhooks;

using Microsoft.AspNetCore.Http;

using Shouldly;

using Xunit;

using WebhookStatus = Foundry.Modules.Monitoring.Domain.ValueObjects.WebhookStatus;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.WebhookTests;

/// <summary>Verification at the boundary, health activation and nudge enqueueing for GitHub deliveries.</summary>
public sealed class WhenDeliveryArrives : IAsyncDisposable
{
    private const string PingBody = """{"zen":"Speak like a human.","repository":{"full_name":"owner/repo"}}""";

    private readonly FoundryWebAppFactory _factory = WebhookTestHost.CreateFactory();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenHookIdUnknown_Returns404()
    {
        // Arrange
        await WebhookTestHost.SeedRepositoryAsync(_factory);

        // Act
        HttpContext response = await WebhookTestHost.DeliverAsync(
            _factory,
            new string('a', MonitoredRepository.WebhookHookIdLength),
            "ping",
            PingBody,
            "any-secret");

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task WhenSignatureMissing_Returns401()
    {
        // Arrange
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, registered: true);

        // Act
        HttpContext response = await WebhookTestHost.DeliverAsync(_factory, repository.WebhookHookId, "ping", PingBody, secret: null);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        (await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookStatus.ShouldBe(WebhookStatus.Pending);
    }

    [Fact]
    public async Task WhenSignedWithWrongSecret_Returns401WithoutParsingBody()
    {
        // Arrange — the body is not even JSON: a 401 proves verification ran before parsing.
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, registered: true);

        // Act
        HttpContext response = await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "issues",
            "not json {",
            "wrong-secret");

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        WebhookTestHost.DrainNudges(_factory).ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenBodyNamesAnotherRepository_Returns400AndEnqueuesNothing()
    {
        // Arrange
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, registered: true);
        string body = """{"action":"opened","issue":{"number":1,"labels":[{"name":"foundry"}]},"repository":{"full_name":"owner/renamed"}}""";

        // Act
        HttpContext response = await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "issues",
            body,
            repository.WebhookSecret);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        WebhookTestHost.DrainNudges(_factory).ShouldBeEmpty();
        (await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookStatus.ShouldBe(WebhookStatus.Pending);
    }

    [Fact]
    public async Task WhenVerifiedPingWhilePending_Returns202AndActivatesHookWithoutNudge()
    {
        // Arrange
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, registered: true);

        // Act — the repository name comparison is case-insensitive.
        HttpContext response = await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "ping",
            """{"zen":"x","repository":{"full_name":"Owner/Repo"}}""",
            repository.WebhookSecret);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status202Accepted);
        (await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookStatus.ShouldBe(WebhookStatus.Active);
        WebhookTestHost.DrainNudges(_factory).ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenVerifiedRelevantIssuesEvent_Returns202ActivatesAndEnqueuesOneListingNudge()
    {
        // Arrange — first delivery is a real event, not ping.
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, registered: true);
        string body = """{"action":"opened","issue":{"number":12,"labels":[{"name":"foundry"}]},"repository":{"full_name":"owner/repo"}}""";

        // Act
        HttpContext response = await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "issues",
            body,
            repository.WebhookSecret);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status202Accepted);
        (await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookStatus.ShouldBe(WebhookStatus.Active);
        WebhookTestHost.DrainNudges(_factory).ShouldBe([new Nudge(new NudgeKey(repository.Id, NudgePass.IssueListing))]);
    }

    [Fact]
    public async Task WhenVerifiedIrrelevantEvent_Returns202AndEnqueuesNothing()
    {
        // Arrange — an issue Foundry does not track and that lacks the trigger label.
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, registered: true);
        string body = """{"action":"opened","issue":{"number":12,"labels":[{"name":"bug"}]},"repository":{"full_name":"owner/repo"}}""";

        // Act
        HttpContext response = await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "issues",
            body,
            repository.WebhookSecret);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status202Accepted);
        WebhookTestHost.DrainNudges(_factory).ShouldBeEmpty();
    }
}
