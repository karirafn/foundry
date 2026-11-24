using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
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

public sealed class RetryJobAsync
{
    private static readonly Uri ApiBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug Slug => RepositorySlug.Create("group/project").ValueOrThrow();

    private static Task<Result> ActAsync(HttpMessageHandler handler) =>
        new GitLabHttpClient(
                new HttpClient(handler),
                NullLogger<GitLabHttpClient>.Instance,
                new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
                new InMemoryProviderRateBudget(),
                TimeProvider.System)
            .RetryJobAsync(ApiBaseUrl, Slug, 42, "token", TestContext.Current.CancellationToken);

    [Fact]
    public async Task WhenCalled_PostsTheJobRetryWithTheToken()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Created, "{}");

        // Act
        Result result = await ActAsync(handler);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Success>(),
            () => request.Method.ShouldBe(HttpMethod.Post),
            () => request.RequestUri!.AbsoluteUri.ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/jobs/42/retry"),
            () => request.Headers.GetValues("PRIVATE-TOKEN").ShouldBe(["token"]));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task WhenTheRetryIsRefused_FailsAsRefused(HttpStatusCode statusCode)
    {
        // Arrange
        FakeHandler handler = new(statusCode, "{}");

        // Act
        Result result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(CiJobRetryErrors.RefusedCode);
    }
}
