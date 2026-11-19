using System.Net;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class UpsertWebhookAsync
{
    private const string HookId = "0123456789abcdef0123456789abcdef";
    private const string PayloadUrl = "https://new.example/webhooks/github/" + HookId;
    private const string OldPayloadUrl = "https://old.example/webhooks/github/" + HookId;
    private const string OlderPayloadUrl = "https://older.example/webhooks/github/" + HookId;
    private const string OtherInstallUrl = "https://new.example/webhooks/github/ffffffffffffffffffffffffffffffff";
    private const string Secret = "s3cr3t-value";

    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static WebhookHookSpec Spec() =>
        new(PayloadUrl, Secret, WebhookHookPath.For(WebhookProvider.GitHub, HookId));

    private static GitHubHttpClient CreateSut(HttpClient httpClient) =>
        new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenNoHookMatches_CreatesOneWithPostAndTheExactEventList()
    {
        // Arrange
        string listJson = """[{ "id": 7, "config": { "url": "https://elsewhere.example.com/hook" } }]""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.Created, """{ "id": 42 }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(new WebhookUpsert(WebhookUpsertKind.Created, 42));
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[0].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Get),
            r => r.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/hooks"),
            r => r.Headers.Authorization!.Parameter.ShouldBe("server_token"));
        handler.Requests[1].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Post),
            r => r.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/hooks"),
            r => r.Headers.Authorization!.Parameter.ShouldBe("server_token"));

        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[1]!);
        JsonElement root = body.RootElement;
        root.GetProperty("name").GetString().ShouldBe("web");
        root.GetProperty("active").GetBoolean().ShouldBeTrue();
        root.GetProperty("events").EnumerateArray().Select(e => e.GetString())
            .ShouldBe(GitHubWebhookEventMapper.SubscribedEvents);
        JsonElement config = root.GetProperty("config");
        config.GetProperty("url").GetString().ShouldBe(PayloadUrl);
        config.GetProperty("content_type").GetString().ShouldBe("json");
        config.GetProperty("secret").GetString().ShouldBe(Secret);
        config.GetProperty("insecure_ssl").GetString().ShouldBe("0");
    }

    [Fact]
    public async Task WhenTheCreateResponseCarriesNoHookId_FailsWithAProviderError()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, """[{ "id": 7, "config": { "url": "https://elsewhere.example.com/hook" } }]"""),
            (HttpStatusCode.Created, "{}"),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<WebhookUpsert>.Failure>()
            .Error.Message.ShouldContain("returned no hook id");
    }

    [Fact]
    public async Task WhenAHookHasThePayloadUrl_UpdatesItInPlaceWithPatch()
    {
        // Arrange
        string listJson = $$"""
            [
              { "id": 7, "config": { "url": "https://elsewhere.example.com/hook" } },
              { "id": 42, "active": false, "config": { "url": "{{PayloadUrl}}" } }
            ]
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.OK, """{ "id": 42 }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(new WebhookUpsert(WebhookUpsertKind.Updated, 42));
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[1].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Patch),
            r => r.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/hooks/42"));
        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[1]!);
        body.RootElement.TryGetProperty("name", out _).ShouldBeFalse();
        body.RootElement.GetProperty("active").GetBoolean().ShouldBeTrue();
        body.RootElement.GetProperty("config").GetProperty("secret").GetString().ShouldBe(Secret);
        body.RootElement.GetProperty("events").GetArrayLength().ShouldBe(GitHubWebhookEventMapper.SubscribedEvents.Count);
    }

    [Fact]
    public async Task WhenHooksAtOlderBaseUrlsCarryTheHookId_UpdatesTheFirstAndDeletesTheRest()
    {
        // Arrange
        string listJson = $$"""
            [
              { "id": 41, "config": { "url": "{{OldPayloadUrl}}" } },
              { "id": 42, "config": { "url": "{{OlderPayloadUrl}}" } }
            ]
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.OK, """{ "id": 41 }"""),
            (HttpStatusCode.NoContent, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(new WebhookUpsert(WebhookUpsertKind.Updated, 41));
        handler.Requests.Count.ShouldBe(3);
        handler.Requests[1].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Patch),
            r => r.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/hooks/41"));
        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[1]!);
        body.RootElement.GetProperty("config").GetProperty("url").GetString().ShouldBe(PayloadUrl);
        handler.Requests[2].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Delete),
            r => r.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/hooks/42"),
            r => r.Headers.Authorization!.Parameter.ShouldBe("server_token"));
    }

    [Fact]
    public async Task WhenOneMatchHasTheExactPayloadUrl_UpdatesThatOneAndDeletesTheOther()
    {
        // Arrange
        string listJson = $$"""
            [
              { "id": 41, "config": { "url": "{{OldPayloadUrl}}" } },
              { "id": 42, "config": { "url": "{{PayloadUrl}}" } }
            ]
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.OK, """{ "id": 42 }"""),
            (HttpStatusCode.NoContent, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        await sut.UpsertWebhookAsync(ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        handler.Requests[1].RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/hooks/42");
        handler.Requests[2].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Delete),
            r => r.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/hooks/41"));
    }

    [Fact]
    public async Task WhenAHookBelongsToAnotherInstall_NeitherUpdatesNorDeletesIt()
    {
        // Arrange
        string listJson = $$"""[{ "id": 99, "config": { "url": "{{OtherInstallUrl}}" } }]""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.Created, """{ "id": 100 }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(new WebhookUpsert(WebhookUpsertKind.Created, 100));
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[1].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Post),
            r => r.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/hooks"));
    }

    [Fact]
    public async Task WhenDeletingADuplicateFails_TheUpsertStillSucceeds()
    {
        // Arrange
        string listJson = $$"""
            [
              { "id": 41, "config": { "url": "{{OldPayloadUrl}}" } },
              { "id": 42, "config": { "url": "{{OlderPayloadUrl}}" } }
            ]
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.OK, """{ "id": 41 }"""),
            (HttpStatusCode.InternalServerError, """{ "message": "boom" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

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
              { "id": 41, "config": { "url": "{{OldPayloadUrl}}" } },
              { "id": 42, "config": { "url": "{{OlderPayloadUrl}}" } }
            ]
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.UnprocessableEntity, """{ "message": "Validation Failed" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<WebhookUpsert>.Failure>();
        handler.Requests.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task WhenListingHooksIsDenied_FailsWithInsufficientPermissionAndWritesNothing(HttpStatusCode status)
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (status, """{ "message": "Resource not accessible by personal access token" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<WebhookUpsert>.Failure>()
            .Error.Code.ShouldBe(WebhookWriteErrors.InsufficientPermissionCode);
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenTheWriteIsRejectedOtherwise_FailsWithAProviderError()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, "[]"),
            (HttpStatusCode.UnprocessableEntity, """{ "message": "Validation Failed" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        Error error = result.ShouldBeOfType<Result<WebhookUpsert>.Failure>().Error;
        error.Code.ShouldNotBe(WebhookWriteErrors.InsufficientPermissionCode);
        error.Message.ShouldContain("422");
        error.Message.ShouldNotContain(Secret);
    }

    [Fact]
    public async Task WhenBaseUrlIsNotHttps_MakesNoCall()
    {
        // Arrange
        SequentialFakeHandler handler = new(Array.Empty<(HttpStatusCode StatusCode, string Body)>());
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsert> result = await sut.UpsertWebhookAsync(
            new Uri("http://api.github.com"), ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<WebhookUpsert>.Failure>();
        handler.Requests.ShouldBeEmpty();
    }
}
