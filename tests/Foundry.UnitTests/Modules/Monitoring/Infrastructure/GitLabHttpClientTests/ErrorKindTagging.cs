using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class ErrorKindTagging
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    [Fact]
    public async Task WhenGitLabReturns404_ErrorKindIsNotFound()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<string> result = await sut.GetPullRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<string>.Failure failure = result.ShouldBeOfType<Result<string>.Failure>();
        failure.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task WhenGitLabReturns500_ErrorKindIsUnknown()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<string> result = await sut.GetPullRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<string>.Failure failure = result.ShouldBeOfType<Result<string>.Failure>();
        failure.Error.Kind.ShouldBe(ErrorKind.Unknown);
    }
}
