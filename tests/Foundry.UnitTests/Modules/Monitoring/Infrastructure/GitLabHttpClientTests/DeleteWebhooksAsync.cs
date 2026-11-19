using System.Net;

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

public sealed class DeleteWebhooksAsync
{
    private const string HookId = "0123456789abcdef0123456789abcdef";

    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static WebhookHookPath HookPath => WebhookHookPath.For(WebhookProvider.GitLab, HookId);

    private static GitLabHttpClient CreateSut(HttpClient httpClient) =>
        new(
            httpClient,
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task DeletesEveryHookCarryingTheHookIdAndLeavesTheRest()
    {
        // Arrange
        string listJson = $$"""
            [
              { "id": 41, "url": "https://old.example/webhooks/gitlab/{{HookId}}" },
              { "id": 99, "url": "https://new.example/webhooks/gitlab/ffffffffffffffffffffffffffffffff" },
              { "id": 42, "url": "https://new.example/webhooks/gitlab/{{HookId}}" }
            ]
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.NoContent, string.Empty),
            (HttpStatusCode.NoContent, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result<int> result = await sut.DeleteWebhooksAsync(
            ValidBaseUrl, ValidSlug, HookPath, "account_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(2);
        handler.Requests.Skip(1).ShouldAllBe(r => r.Method == HttpMethod.Delete);
        handler.Requests.Skip(1).Select(r => r.RequestUri!.AbsoluteUri.Replace("https://gitlab.com", string.Empty, StringComparison.Ordinal))
            .ShouldBe(["/api/v4/projects/group%2Fproject/hooks/41", "/api/v4/projects/group%2Fproject/hooks/42"]);
    }

    [Fact]
    public async Task WhenNoHookMatches_DeletesNothing()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, """[{ "id": 7, "url": "https://elsewhere.example.com/hook" }]"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result<int> result = await sut.DeleteWebhooksAsync(
            ValidBaseUrl, ValidSlug, HookPath, "account_token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(0);
        handler.Requests.Count.ShouldBe(1);
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
            (HttpStatusCode.OK, $$"""[{ "id": 41, "url": "https://old.example/webhooks/gitlab/{{HookId}}" }]"""),
            (HttpStatusCode.InternalServerError, """{ "message": "boom" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result<int> result = await sut.DeleteWebhooksAsync(
            ValidBaseUrl, ValidSlug, HookPath, "account_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<int>.Failure>().Error.Message.ShouldContain("500");
    }
}
