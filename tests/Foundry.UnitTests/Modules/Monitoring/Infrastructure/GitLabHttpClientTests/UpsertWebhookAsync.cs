using System.Net;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Modules.Monitoring.Features.Webhooks.Delivery;
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
    private const string HookId = "0123456789abcdef0123456789abcdef";
    private const string PayloadUrl = "https://new.example/webhooks/gitlab/" + HookId;
    private const string OldPayloadUrl = "https://old.example/webhooks/gitlab/" + HookId;
    private const string OlderPayloadUrl = "https://older.example/webhooks/gitlab/" + HookId;
    private const string OtherInstallUrl = "https://new.example/webhooks/gitlab/ffffffffffffffffffffffffffffffff";
    private const string Secret = "s3cr3t-value";

    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static WebhookHookSpec Spec() =>
        new(PayloadUrl, Secret, WebhookHookPath.For(WebhookProvider.GitLab, HookId));

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
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(new WebhookUpsert(WebhookUpsertKind.Created, 42));
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
        root.GetProperty("push_events").GetBoolean().ShouldBeTrue();
        root.GetProperty("tag_push_events").GetBoolean().ShouldBeFalse();

        string[] enabled = root.EnumerateObject()
            .Where(p => p.Name.EndsWith("_events", StringComparison.Ordinal) && p.Value.ValueKind == JsonValueKind.True)
            .Select(p => p.Name)
            .ToArray();
        enabled.ShouldBe(GitLabWebhookEventMapper.SubscribedTriggers, ignoreOrder: true);
    }

    [Fact]
    public async Task WhenTheCreateResponseCarriesNoHookId_FailsWithAProviderError()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, """[{ "id": 7, "url": "https://elsewhere.example.com/hook" }]"""),
            (HttpStatusCode.Created, "{}"),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<WebhookUpsert>.Failure>()
            .Error.Message.ShouldContain("returned no hook id");
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
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(new WebhookUpsert(WebhookUpsertKind.Updated, 42));
        handler.Requests[1].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Put),
            r => r.RequestUri!.AbsoluteUri.ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/hooks/42"));
    }

    [Fact]
    public async Task WhenHooksAtOlderBaseUrlsCarryTheHookId_UpdatesTheFirstAndDeletesTheRest()
    {
        // Arrange
        string listJson = $$"""
            [
              { "id": 41, "url": "{{OldPayloadUrl}}" },
              { "id": 42, "url": "{{OlderPayloadUrl}}" }
            ]
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.OK, """{ "id": 41 }"""),
            (HttpStatusCode.NoContent, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(new WebhookUpsert(WebhookUpsertKind.Updated, 41));
        handler.Requests.Count.ShouldBe(3);
        handler.Requests[1].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Put),
            r => r.RequestUri!.AbsoluteUri.ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/hooks/41"));
        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[1]!);
        body.RootElement.GetProperty("url").GetString().ShouldBe(PayloadUrl);
        handler.Requests[2].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Delete),
            r => r.RequestUri!.AbsoluteUri.ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/hooks/42"),
            r => r.Headers.GetValues("PRIVATE-TOKEN").ShouldBe(["server_token"]));
    }

    [Fact]
    public async Task WhenOneMatchHasTheExactPayloadUrl_UpdatesThatOneAndDeletesTheOther()
    {
        // Arrange
        string listJson = $$"""
            [
              { "id": 41, "url": "{{OldPayloadUrl}}" },
              { "id": 42, "url": "{{PayloadUrl}}" }
            ]
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.OK, """{ "id": 42 }"""),
            (HttpStatusCode.NoContent, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        await sut.UpsertWebhookAsync(ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        handler.Requests[1].RequestUri!.AbsoluteUri.ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/hooks/42");
        handler.Requests[2].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Delete),
            r => r.RequestUri!.AbsoluteUri.ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/hooks/41"));
    }

    [Fact]
    public async Task WhenAHookBelongsToAnotherInstall_NeitherUpdatesNorDeletesIt()
    {
        // Arrange
        string listJson = $$"""[{ "id": 99, "url": "{{OtherInstallUrl}}" }]""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.Created, """{ "id": 100 }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(new WebhookUpsert(WebhookUpsertKind.Created, 100));
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[1].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Post),
            r => r.RequestUri!.AbsoluteUri.ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/hooks"));
    }

    [Fact]
    public async Task WhenDeletingADuplicateFails_TheUpsertStillSucceeds()
    {
        // Arrange
        string listJson = $$"""
            [
              { "id": 41, "url": "{{OldPayloadUrl}}" },
              { "id": 42, "url": "{{OlderPayloadUrl}}" }
            ]
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.OK, """{ "id": 41 }"""),
            (HttpStatusCode.InternalServerError, """{ "message": "boom" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(new WebhookUpsert(WebhookUpsertKind.Updated, 41));
        handler.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task WhenTheUpdateFails_DeletesNothing()
    {
        // Arrange
        string listJson = $$"""
            [
              { "id": 41, "url": "{{OldPayloadUrl}}" },
              { "id": 42, "url": "{{OlderPayloadUrl}}" }
            ]
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.UnprocessableEntity, """{ "message": "Invalid url" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<WebhookUpsert>.Failure>();
        handler.Requests.Count.ShouldBe(2);
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
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<WebhookUpsert>.Failure>()
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
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<WebhookUpsert>.Failure>()
            .Error.Code.ShouldNotBe(WebhookWriteErrors.InsufficientPermissionCode);
    }
}
