using System.Net;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class UpsertWebhookAsync
{
    private const string PayloadUrl = "https://hooks.example.com/webhooks/gitlab/abc";
    private const string OldPayloadUrl = "https://old.example.com/webhooks/gitlab/abc";
    private const string Secret = "s3cr3t-value";

    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static WebhookHookSpec Spec(params string[] previous) => new(PayloadUrl, Secret, previous);

    private static GitLabHttpClient CreateSut(HttpClient httpClient) =>
        new(
            httpClient,
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenNoHookMatches_CreatesOneWithPostAndExactlyTheMappedTriggers()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, """[{ "id": 7, "url": "https://elsewhere.example.com/hook" }]"""),
            (HttpStatusCode.Created, """{ "id": 42 }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsertKind> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(WebhookUpsertKind.Created);
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[0].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Get),
            r => r.RequestUri!.AbsoluteUri.ShouldStartWith("https://gitlab.com/api/v4/projects/group%2Fproject/hooks"));
        handler.Requests[1].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Post),
            r => r.RequestUri!.AbsoluteUri.ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/hooks"));

        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[1]!);
        JsonElement root = body.RootElement;
        root.GetProperty("url").GetString().ShouldBe(PayloadUrl);
        root.GetProperty("token").GetString().ShouldBe(Secret);
        root.GetProperty("enable_ssl_verification").GetBoolean().ShouldBeTrue();
        root.GetProperty("push_events").GetBoolean().ShouldBeFalse();

        string[] enabled = root.EnumerateObject()
            .Where(p => p.Name.EndsWith("_events", StringComparison.Ordinal) && p.Value.ValueKind == JsonValueKind.True)
            .Select(p => p.Name)
            .ToArray();
        enabled.ShouldBe(GitLabWebhookEventMapper.SubscribedTriggers, ignoreOrder: true);
    }

    [Fact]
    public async Task WhenAHookHasThePayloadUrl_UpdatesItInPlaceWithPut()
    {
        // Arrange
        string listJson = $$"""[{ "id": 42, "url": "{{PayloadUrl}}" }]""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.OK, """{ "id": 42 }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsertKind> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(WebhookUpsertKind.Updated);
        handler.Requests[1].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Put),
            r => r.RequestUri!.AbsoluteUri.ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/hooks/42"));
    }

    [Fact]
    public async Task WhenAHookHasAPreviousPayloadUrl_RepointsIt()
    {
        // Arrange
        string listJson = $$"""[{ "id": 42, "url": "{{OldPayloadUrl}}" }]""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.OK, """{ "id": 42 }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsertKind> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(OldPayloadUrl), "server_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(WebhookUpsertKind.Updated);
        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[1]!);
        body.RootElement.GetProperty("url").GetString().ShouldBe(PayloadUrl);
    }

    [Fact]
    public async Task WhenBelowMaintainer_FailsWithInsufficientPermission()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.Forbidden, """{ "message": "403 Forbidden" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsertKind> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<WebhookUpsertKind>.Failure>()
            .Error.Code.ShouldBe(WebhookWriteErrors.InsufficientPermissionCode);
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenNotFound_FailsWithAProviderError()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.NotFound, """{ "message": "404 Project Not Found" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsertKind> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<WebhookUpsertKind>.Failure>()
            .Error.Code.ShouldNotBe(WebhookWriteErrors.InsufficientPermissionCode);
    }
}
