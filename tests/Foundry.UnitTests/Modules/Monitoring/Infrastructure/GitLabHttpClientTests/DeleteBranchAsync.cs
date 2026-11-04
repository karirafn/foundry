using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class DeleteBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient BuildSut(HttpClient httpClient) =>
        new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

    [Fact]
    public async Task WhenBranchDeleted_ReturnsTrueAndDeletesTheEncodedBranch()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.NoContent, string.Empty)]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = BuildSut(httpClient);

        // Act
        Result<bool> result = await sut.DeleteBranchAsync(
            ValidBaseUrl, ValidSlug, "feat/42-add-retry", "glpat_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<bool>.Success>().Value.ShouldBeTrue();
        HttpRequestMessage request = handler.Requests.ShouldHaveSingleItem();
        request.ShouldSatisfyAllConditions(
            () => request.Method.ShouldBe(HttpMethod.Delete),
            () => request.RequestUri!.AbsoluteUri.ShouldEndWith(
                "/projects/group%2Fproject/repository/branches/feat%2F42-add-retry"));
    }

    [Fact]
    public async Task WhenBranchAlreadyAbsent_ReturnsFalse()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.NotFound, """{ "message": "404 Branch Not Found" }""")]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = BuildSut(httpClient);

        // Act
        Result<bool> result = await sut.DeleteBranchAsync(
            ValidBaseUrl, ValidSlug, "feat/42-add-retry", "glpat_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<bool>.Success>().Value.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenProviderRejectsTheDelete_ReturnsFailure()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.Forbidden, string.Empty)]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = BuildSut(httpClient);

        // Act
        Result<bool> result = await sut.DeleteBranchAsync(
            ValidBaseUrl, ValidSlug, "feat/42-add-retry", "glpat_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<bool>.Failure>();
    }
}
