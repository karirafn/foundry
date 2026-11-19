using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
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

public sealed class TestWebhookAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient CreateSut(HttpClient httpClient) =>
        new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task PostsToTheHooksPingEndpointWithTheToken()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.NoContent, string.Empty)]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.TestWebhookAsync(ValidBaseUrl, ValidSlug, 42, "account_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result.Success>();
        handler.Requests.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Post),
            r => r.RequestUri!.AbsoluteUri.ShouldBe("https://api.github.com/repos/owner/repo/hooks/42/pings"),
            r => r.Headers.Authorization!.Parameter.ShouldBe("account_token"));
    }

    [Fact]
    public async Task WhenTheHookIsNotFound_Fails()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.NotFound, """{ "message": "Not Found" }""")]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.TestWebhookAsync(ValidBaseUrl, ValidSlug, 42, "account_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Message.ShouldContain("404");
    }

    [Fact]
    public async Task WhenTheBaseUrlIsNotHttps_FailsWithoutACall()
    {
        // Arrange
        SequentialFakeHandler handler = new(Array.Empty<(HttpStatusCode, string)>());
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.TestWebhookAsync(
            new Uri("http://api.github.com"), ValidSlug, 42, "account_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.ShouldBe(GitHubErrors.InvalidBaseUrl);
        handler.Requests.ShouldBeEmpty();
    }
}
