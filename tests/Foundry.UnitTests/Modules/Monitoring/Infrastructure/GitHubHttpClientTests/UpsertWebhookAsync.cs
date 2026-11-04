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
    private const string PayloadUrl = "https://hooks.example.com/webhooks/github/abc";
    private const string OldPayloadUrl = "https://old.example.com/webhooks/github/abc";
    private const string Secret = "s3cr3t-value";

    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static WebhookHookSpec Spec(params string[] previous) => new(PayloadUrl, Secret, previous);

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
        Result<WebhookUpsertKind> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(WebhookUpsertKind.Created);
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
        Result<WebhookUpsertKind> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(WebhookUpsertKind.Updated);
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
    public async Task WhenAHookHasAPreviousPayloadUrl_RepointsItInsteadOfDuplicating()
    {
        // Arrange
        string listJson = $$"""[{ "id": 42, "config": { "url": "{{OldPayloadUrl}}" } }]""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.OK, """{ "id": 42 }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result<WebhookUpsertKind> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(OldPayloadUrl), "server_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(WebhookUpsertKind.Updated);
        handler.Requests[1].RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/hooks/42");
        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[1]!);
        body.RootElement.GetProperty("config").GetProperty("url").GetString().ShouldBe(PayloadUrl);
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
        Result<WebhookUpsertKind> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<WebhookUpsertKind>.Failure>()
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
        Result<WebhookUpsertKind> result = await sut.UpsertWebhookAsync(
            ValidBaseUrl, ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        Error error = result.ShouldBeOfType<Result<WebhookUpsertKind>.Failure>().Error;
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
        Result<WebhookUpsertKind> result = await sut.UpsertWebhookAsync(
            new Uri("http://api.github.com"), ValidSlug, Spec(), "server_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<WebhookUpsertKind>.Failure>();
        handler.Requests.ShouldBeEmpty();
    }
}
