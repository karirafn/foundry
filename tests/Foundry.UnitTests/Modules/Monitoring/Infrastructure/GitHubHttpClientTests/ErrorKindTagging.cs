using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class ErrorKindTagging
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    [Fact]
    public async Task WhenGitHubReturns404_ErrorKindIsNotFound()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<string> result = await sut.GetPullRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<string>.Failure failure = result.ShouldBeOfType<Result<string>.Failure>();
        failure.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task WhenGitHubReturns500_ErrorKindIsUnknown()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<string> result = await sut.GetPullRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<string>.Failure failure = result.ShouldBeOfType<Result<string>.Failure>();
        failure.Error.Kind.ShouldBe(ErrorKind.Unknown);
    }
}
