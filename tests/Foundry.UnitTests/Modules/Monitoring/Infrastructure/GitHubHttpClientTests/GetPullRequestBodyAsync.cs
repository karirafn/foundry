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

public sealed class GetPullRequestBodyAsync
{
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/1218";

    // Captured 2026-11-20 from GET repos/karirafn/foundry/pulls/1218, cut to four fields and the body to 60 characters.
    private const string CapturedWithBody = """
        {"number":1218,"state":"closed","title":"feat: A review criterion covered by passing checks counts as met","body":"> [!WARNING]\n> - Once a verdict with covered_by_checks is st"}
        """;

    // Captured 2026-11-20 from GET repos/octocat/Hello-World/pulls/11755, cut to four fields.
    private const string CapturedWithNullBody = """
        {"number":11755,"state":"closed","title":"probe","body":null}
        """;

    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug => RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient CreateSut(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenCalled_RequestsThePullRequest()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, CapturedWithBody);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        await sut.GetPullRequestBodyAsync(ValidBaseUrl, ValidSlug, PullRequestUrl, "token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.Method.ShouldBe(HttpMethod.Get);
        request.RequestUri.ShouldNotBeNull().AbsolutePath.ShouldBe("/repos/owner/repo/pulls/1218");
    }

    [Fact]
    public async Task WhenThePullRequestHasABody_ReturnsIt()
    {
        // Arrange
        GitHubHttpClient sut = CreateSut(new FakeHandler(HttpStatusCode.OK, CapturedWithBody));

        // Act
        Result<string> result = await sut.GetPullRequestBodyAsync(
            ValidBaseUrl, ValidSlug, PullRequestUrl, "token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe("> [!WARNING]\n> - Once a verdict with covered_by_checks is st");
    }

    [Fact]
    public async Task WhenTheBodyIsNull_ReturnsEmpty()
    {
        // Arrange
        GitHubHttpClient sut = CreateSut(new FakeHandler(HttpStatusCode.OK, CapturedWithNullBody));

        // Act
        Result<string> result = await sut.GetPullRequestBodyAsync(
            ValidBaseUrl, ValidSlug, PullRequestUrl, "token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenGitHubFails_ReturnsFailure()
    {
        // Arrange
        GitHubHttpClient sut = CreateSut(new FakeHandler(HttpStatusCode.NotFound, string.Empty));

        // Act
        Result<string> result = await sut.GetPullRequestBodyAsync(
            ValidBaseUrl, ValidSlug, PullRequestUrl, "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>().Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task WhenTheUrlIsNotAPullRequest_FailsWithoutARequest()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, CapturedWithBody);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<string> result = await sut.GetPullRequestBodyAsync(
            ValidBaseUrl, ValidSlug, "https://github.com/owner/repo/issues/12", "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>();
        handler.LastRequest.ShouldBeNull();
    }
}
