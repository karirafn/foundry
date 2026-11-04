using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class DeleteBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(HttpClient httpClient) =>
        new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

    [Fact]
    public async Task WhenBranchDeleted_ReturnsTrueAndDeletesTheBranchRef()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.NoContent, string.Empty)]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = BuildSut(httpClient);

        // Act
        Result<bool> result = await sut.DeleteBranchAsync(
            ValidBaseUrl, ValidSlug, "feat/42-add-retry", "ghp_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<bool>.Success>().Value.ShouldBeTrue();
        HttpRequestMessage request = handler.Requests.ShouldHaveSingleItem();
        request.ShouldSatisfyAllConditions(
            () => request.Method.ShouldBe(HttpMethod.Delete),
            () => request.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/git/refs/heads/feat/42-add-retry"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task WhenBranchAlreadyAbsent_ReturnsFalse(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(statusCode, """{ "message": "Reference does not exist" }""")]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = BuildSut(httpClient);

        // Act
        Result<bool> result = await sut.DeleteBranchAsync(
            ValidBaseUrl, ValidSlug, "feat/42-add-retry", "ghp_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<bool>.Success>().Value.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenProviderRejectsTheDelete_ReturnsFailure()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.Forbidden, string.Empty)]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = BuildSut(httpClient);

        // Act
        Result<bool> result = await sut.DeleteBranchAsync(
            ValidBaseUrl, ValidSlug, "feat/42-add-retry", "ghp_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<bool>.Failure>();
    }

    [Fact]
    public async Task WhenBaseUrlIsNotHttps_ReturnsInvalidBaseUrlWithoutCallingProvider()
    {
        // Arrange
        SequentialFakeHandler handler = new(Array.Empty<(HttpStatusCode, string)>());
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = BuildSut(httpClient);

        // Act
        Result<bool> result = await sut.DeleteBranchAsync(
            new Uri("http://api.github.com"), ValidSlug, "feat/42-add-retry", "ghp_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<bool>.Failure>().Error.ShouldBe(GitHubErrors.InvalidBaseUrl);
        handler.Requests.ShouldBeEmpty();
    }
}
