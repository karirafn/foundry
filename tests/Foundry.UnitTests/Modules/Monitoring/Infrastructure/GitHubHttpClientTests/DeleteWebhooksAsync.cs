using System.Net;

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

public sealed class DeleteWebhooksAsync
{
    private const string HookId = "0123456789abcdef0123456789abcdef";

    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static WebhookHookPath HookPath => WebhookHookPath.For(WebhookProvider.GitHub, HookId);

    private static GitHubHttpClient CreateSut(HttpClient httpClient) =>
        new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task DeletesEveryHookCarryingTheHookIdAndLeavesTheRest()
    {
        // Arrange
        string listJson = $$"""
            [
              { "id": 41, "config": { "url": "https://old.example/webhooks/github/{{HookId}}" } },
              { "id": 99, "config": { "url": "https://new.example/webhooks/github/ffffffffffffffffffffffffffffffff" } },
              { "id": 42, "config": { "url": "https://new.example/webhooks/github/{{HookId}}" } }
            ]
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.NoContent, string.Empty),
            (HttpStatusCode.NoContent, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result<int> result = await sut.DeleteWebhooksAsync(
            ValidBaseUrl, ValidSlug, HookPath, "account_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(2);
        handler.Requests.Skip(1).ShouldAllBe(r => r.Method == HttpMethod.Delete);
        handler.Requests.Skip(1).Select(r => r.RequestUri!.AbsolutePath)
            .ShouldBe(["/repos/owner/repo/hooks/41", "/repos/owner/repo/hooks/42"]);
    }

    [Fact]
    public async Task WhenNoHookMatches_DeletesNothing()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, """[{ "id": 7, "config": { "url": "https://elsewhere.example.com/hook" } }]"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result<int> result = await sut.DeleteWebhooksAsync(
            ValidBaseUrl, ValidSlug, HookPath, "account_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(0);
        handler.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task WhenListingHooksIsDenied_FailsWithInsufficientPermission(HttpStatusCode status)
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (status, """{ "message": "Resource not accessible by personal access token" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result<int> result = await sut.DeleteWebhooksAsync(
            ValidBaseUrl, ValidSlug, HookPath, "account_token", CancellationToken.None);

        // Assert
        Error error = result.ShouldBeOfType<Result<int>.Failure>().Error;
        error.Code.ShouldBe(WebhookWriteErrors.InsufficientPermissionCode);
        error.Message.ShouldStartWith("Deleting the webhooks");
    }

    [Fact]
    public async Task WhenADeleteFails_FailsWithTheProviderError()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, $$"""[{ "id": 41, "config": { "url": "https://old.example/webhooks/github/{{HookId}}" } }]"""),
            (HttpStatusCode.InternalServerError, """{ "message": "boom" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result<int> result = await sut.DeleteWebhooksAsync(
            ValidBaseUrl, ValidSlug, HookPath, "account_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<int>.Failure>().Error.Message.ShouldContain("500");
    }
}
