using System.Net;

using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class ListRepositoriesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static string BuildPageJson(int count, int startIndex = 0)
    {
        string items = string.Join(
            ",",
            Enumerable
                .Range(startIndex, count)
                .Select(i => $$"""{"path_with_namespace":"group/project-{{i}}","visibility":"public"}"""));
        return $"[{items}]";
    }

    [Fact]
    public async Task WhenGitLabReturnsSinglePage_ParsesRepositoriesCorrectly()
    {
        // Arrange
        string json = """
            [
              {
                "path_with_namespace": "alice/public-project",
                "visibility": "public"
              },
              {
                "path_with_namespace": "alice/secret-project",
                "visibility": "private"
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<AvailableRepository>> result = await sut.ListRepositoriesAsync(
            ValidBaseUrl,
            "glpat_token123",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<AvailableRepository>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<AvailableRepository>>.Success>();
        IReadOnlyList<AvailableRepository> repos = success.Value;
        repos.Count.ShouldBe(2);
        repos.ShouldContain(r => r.Slug == "alice/public-project" && !r.IsPrivate);
        repos.ShouldContain(r => r.Slug == "alice/secret-project" && r.IsPrivate);
    }

    [Fact]
    public async Task WhenGitLabReturnsMultipleFullPages_PaginatesAndCombinesAllResults()
    {
        // Arrange
        string page1Json = BuildPageJson(100, startIndex: 0);
        string page2Json = BuildPageJson(50, startIndex: 100);

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, page1Json),
            (HttpStatusCode.OK, page2Json),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<AvailableRepository>> result = await sut.ListRepositoriesAsync(
            ValidBaseUrl,
            "glpat_token123",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<AvailableRepository>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<AvailableRepository>>.Success>();
        success.Value.Count.ShouldBe(150);
    }

    [Fact]
    public async Task WhenAllPagesAreFull_StopsAfterFivePages()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, BuildPageJson(100, startIndex: 0)),
            (HttpStatusCode.OK, BuildPageJson(100, startIndex: 100)),
            (HttpStatusCode.OK, BuildPageJson(100, startIndex: 200)),
            (HttpStatusCode.OK, BuildPageJson(100, startIndex: 300)),
            (HttpStatusCode.OK, BuildPageJson(100, startIndex: 400)),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<AvailableRepository>> result = await sut.ListRepositoriesAsync(
            ValidBaseUrl,
            "glpat_token123",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<AvailableRepository>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<AvailableRepository>>.Success>();
        success.Value.Count.ShouldBe(500);
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectEndpointUrl()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        await sut.ListRepositoriesAsync(ValidBaseUrl, "glpat_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.IsAbsoluteUri.ShouldBeTrue();
        request.RequestUri.AbsolutePath.ShouldBe("/api/v4/projects");
        request.RequestUri.Query.ShouldContain("membership=true");
        request.RequestUri.Query.ShouldContain("simple=true");
        request.RequestUri.Query.ShouldContain("per_page=100");
        request.RequestUri.Query.ShouldContain("page=1");
    }

    [Fact]
    public async Task WhenCalled_UsesPrivateTokenHeader()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        await sut.ListRepositoriesAsync(ValidBaseUrl, "glpat_mytoken", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.Headers.TryGetValues("PRIVATE-TOKEN", out IEnumerable<string>? values).ShouldBeTrue();
        values.ShouldNotBeNull();
        values.FirstOrDefault().ShouldBe("glpat_mytoken");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);
        Uri invalidBaseUrl = new("ftp://gitlab.com/api/v4");

        // Act
        Result<IReadOnlyList<AvailableRepository>> result = await sut.ListRepositoriesAsync(
            invalidBaseUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<AvailableRepository>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<AvailableRepository>>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenGitLabReturnsNonSuccessStatus_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<AvailableRepository>> result = await sut.ListRepositoriesAsync(
            ValidBaseUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<AvailableRepository>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<AvailableRepository>>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }
}
