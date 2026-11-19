using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
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

public sealed class TestWebhookAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient CreateSut(HttpClient httpClient) =>
        new(
            httpClient,
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task PostsToTheHooksTestPushEventsEndpointWithTheToken()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.Created, """{ "message": "201 Created" }""")]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.TestWebhookAsync(ValidBaseUrl, ValidSlug, 42, "account_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result.Success>();
        handler.Requests.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Post),
            r => r.RequestUri!.AbsoluteUri.ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/hooks/42/test/push_events"),
            r => r.Headers.GetValues("PRIVATE-TOKEN").ShouldBe(["account_token"]));
    }

    [Fact]
    public async Task WhenTheHookIsNotFound_Fails()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.NotFound, """{ "message": "Not Found" }""")]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

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
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.TestWebhookAsync(
            new Uri("http://gitlab.com/api/v4"), ValidSlug, 42, "account_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.ShouldBe(GitLabErrors.InvalidBaseUrl);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheTestRateLimitIsHit_Fails()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.TooManyRequests, """{ "message": "This endpoint has been requested too many times. Try again later." }""")]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.TestWebhookAsync(ValidBaseUrl, ValidSlug, 42, "account_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result.Failure>();
        handler.Requests.Count.ShouldBe(1);
    }
}
