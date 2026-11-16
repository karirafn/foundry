using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.RootDirectory;
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

public sealed class ListRootDirectoryAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    // Shape of GitLab's "List repository tree" response (REST docs, repository/tree), with a symlink
    // (mode 120000) and a submodule (type commit) added as GitLab reports them.
    private const string TreeJson = """
        [
          { "id": "a1e8f8d745cc87e3a9248358d9352bb7f9a0aeba", "name": "nuget.config", "type": "blob", "path": "nuget.config", "mode": "100644" },
          { "id": "4535904260b1082e14f867f7a24fd8c21495bde3", "name": "src", "type": "tree", "path": "src", "mode": "040000" },
          { "id": "ab09011fa121d0a2bb9fa4ca76094f2482b902b7", "name": "link", "type": "blob", "path": "link", "mode": "120000" },
          { "id": "7c258a9869f33c1e1e1f74fbb32f07c86cb5a75b", "name": "vendor", "type": "commit", "path": "vendor", "mode": "160000" }
        ]
        """;

    private static RepositorySlug ValidSlug => RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient CreateSut(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenCalled_ListsTheDefaultBranchRootWithoutRecursion()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, TreeJson);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        await sut.ListRootDirectoryAsync(ValidBaseUrl, ValidSlug, ifNoneMatch: null, "token", CancellationToken.None);

        // Assert
        Uri requestUri = handler.LastRequest.ShouldNotBeNull().RequestUri.ShouldNotBeNull();
        requestUri.AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/repository/tree");
        requestUri.Query.ShouldContain("per_page=100");
        requestUri.Query.ShouldNotContain("recursive");
        requestUri.Query.ShouldNotContain("ref=");
    }

    [Fact]
    public async Task WhenTreeIsReturned_MapsEachEntryNameKindAndSha()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, TreeJson);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<RootDirectoryListing> result = await sut.ListRootDirectoryAsync(
            ValidBaseUrl, ValidSlug, ifNoneMatch: null, "token", CancellationToken.None);

        // Assert
        RootDirectoryListing.Listed listed = result.ValueOrThrow().ShouldBeOfType<RootDirectoryListing.Listed>();
        listed.ETag.ShouldBeNull();
        listed.Entries.ShouldBe(
        [
            new RootEntry("nuget.config", RootEntryKind.File, "a1e8f8d745cc87e3a9248358d9352bb7f9a0aeba"),
            new RootEntry("src", RootEntryKind.Directory, "4535904260b1082e14f867f7a24fd8c21495bde3"),
            new RootEntry("link", RootEntryKind.Symlink, "ab09011fa121d0a2bb9fa4ca76094f2482b902b7"),
            new RootEntry("vendor", RootEntryKind.Submodule, "7c258a9869f33c1e1e1f74fbb32f07c86cb5a75b"),
        ]);
    }

    [Fact]
    public async Task WhenGitLabReportsANextPage_FollowsIt()
    {
        // Arrange
        string firstPage = "[" + string.Join(",", Enumerable.Range(1, 100).Select(n =>
            $$"""{ "id": "{{n:x40}}", "name": "file{{n}}", "type": "blob", "path": "file{{n}}", "mode": "100644" }""")) + "]";
        string secondPage = """[{ "id": "a1e8f8d745cc87e3a9248358d9352bb7f9a0aeba", "name": "nuget.config", "type": "blob", "path": "nuget.config", "mode": "100644" }]""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, firstPage, new Dictionary<string, string> { ["X-Next-Page"] = "2" }),
            (HttpStatusCode.OK, secondPage, new Dictionary<string, string> { ["X-Next-Page"] = "" }),
        ]);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<RootDirectoryListing> result = await sut.ListRootDirectoryAsync(
            ValidBaseUrl, ValidSlug, ifNoneMatch: null, "token", CancellationToken.None);

        // Assert
        RootDirectoryListing.Listed listed = result.ValueOrThrow().ShouldBeOfType<RootDirectoryListing.Listed>();
        listed.Entries.Count.ShouldBe(101);
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[1].RequestUri.ShouldNotBeNull().Query.ShouldContain("page=2");
    }

    [Fact]
    public async Task WhenAPageIsNotFull_StopsWithoutAnotherCall()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, TreeJson, new Dictionary<string, string> { ["X-Next-Page"] = "" }),
        ]);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        await sut.ListRootDirectoryAsync(ValidBaseUrl, ValidSlug, ifNoneMatch: null, "token", CancellationToken.None);

        // Assert
        handler.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenGitLabFails_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<RootDirectoryListing> result = await sut.ListRootDirectoryAsync(
            ValidBaseUrl, ValidSlug, ifNoneMatch: null, "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<RootDirectoryListing>.Failure>().Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenBaseUrlIsNotHttps_ReturnsFailureWithoutACall()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, TreeJson);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<RootDirectoryListing> result = await sut.ListRootDirectoryAsync(
            new Uri("http://gitlab.com/api/v4"), ValidSlug, ifNoneMatch: null, "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<RootDirectoryListing>.Failure>().Error.ShouldBe(GitLabErrors.InvalidBaseUrl);
        handler.AllRequests.ShouldBeEmpty();
    }
}
