using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
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

public sealed class RerunJobAsync
{
    private static readonly Uri ApiBaseUrl = new("https://api.github.com");

    private static RepositorySlug Slug => RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static Task<Result> ActAsync(HttpMessageHandler handler) =>
        new GitHubHttpClient(
                new HttpClient(handler),
                NullLogger<GitHubHttpClient>.Instance,
                new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
                new InMemoryProviderRateBudget(),
                TimeProvider.System)
            .RerunJobAsync(ApiBaseUrl, Slug, 42, "token", TestContext.Current.CancellationToken);

    [Fact]
    public async Task WhenCalled_PostsTheJobRerunWithTheToken()
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
            () => request.RequestUri!.AbsoluteUri.ShouldBe("https://api.github.com/repos/owner/repo/actions/jobs/42/rerun"),
            () => request.Headers.Authorization!.Parameter.ShouldBe("token"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task WhenTheRerunIsRefused_FailsAsRefused(HttpStatusCode statusCode)
    {
        // Arrange
        FakeHandler handler = new(statusCode, "{}");

        // Act
        Result result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(CiJobRetryErrors.RefusedCode);
    }
}
