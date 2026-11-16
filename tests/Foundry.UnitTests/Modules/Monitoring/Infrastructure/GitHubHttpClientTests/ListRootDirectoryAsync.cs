using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.RootDirectory;
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

public sealed class ListRootDirectoryAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    // Shape of GitHub's "Get a tree" response (REST docs, git/trees), with a symlink (mode 120000) and a
    // submodule (type commit) added as GitHub reports them.
    private const string TreeJson = """
        {
          "sha": "9fb037999f264ba9a7fc6274d15fa3ae2ab98312",
          "url": "https://api.github.com/repos/owner/repo/git/trees/9fb037999f264ba9a7fc6274d15fa3ae2ab98312",
          "tree": [
            { "path": "NuGet.Config", "mode": "100644", "type": "blob", "size": 30, "sha": "44b4fc6d56897b048c772eb4087f854f46256132" },
            { "path": "src", "mode": "040000", "type": "tree", "sha": "f484d249c660418515fb01c2b9662073663c242e" },
            { "path": "link", "mode": "120000", "type": "blob", "size": 12, "sha": "45b983be36b73c0788dc9cbcb76cbb80fc7bb057" },
            { "path": "vendor", "mode": "160000", "type": "commit", "sha": "7c258a9869f33c1e1e1f74fbb32f07c86cb5a75b" }
          ],
          "truncated": false
        }
        """;

    private static RepositorySlug ValidSlug => RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient CreateSut(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenCalled_ListsTheDefaultBranchRootTreeWithoutRecursion()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, TreeJson);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        await sut.ListRootDirectoryAsync(ValidBaseUrl, ValidSlug, ifNoneMatch: null, "token", CancellationToken.None);

        // Assert
        Uri requestUri = handler.LastRequest.ShouldNotBeNull().RequestUri.ShouldNotBeNull();
        requestUri.AbsolutePath.ShouldBe("/repos/owner/repo/git/trees/HEAD");
        requestUri.Query.ShouldNotContain("recursive");
    }

    [Fact]
    public async Task WhenTreeIsReturned_MapsEachEntryNameKindAndSha()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, TreeJson);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<RootDirectoryListing> result = await sut.ListRootDirectoryAsync(
            ValidBaseUrl, ValidSlug, ifNoneMatch: null, "token", CancellationToken.None);

        // Assert
        RootDirectoryListing.Listed listed = result.ValueOrThrow().ShouldBeOfType<RootDirectoryListing.Listed>();
        listed.Entries.ShouldBe(
        [
            new RootEntry("NuGet.Config", RootEntryKind.File, "44b4fc6d56897b048c772eb4087f854f46256132"),
            new RootEntry("src", RootEntryKind.Directory, "f484d249c660418515fb01c2b9662073663c242e"),
            new RootEntry("link", RootEntryKind.Symlink, "45b983be36b73c0788dc9cbcb76cbb80fc7bb057"),
            new RootEntry("vendor", RootEntryKind.Submodule, "7c258a9869f33c1e1e1f74fbb32f07c86cb5a75b"),
        ]);
    }

    [Fact]
    public async Task WhenResponseCarriesAnETag_ReturnsIt()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, TreeJson);
        handler.ResponseHeaders["ETag"] = "W/\"0123abcd\"";
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<RootDirectoryListing> result = await sut.ListRootDirectoryAsync(
            ValidBaseUrl, ValidSlug, ifNoneMatch: null, "token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBeOfType<RootDirectoryListing.Listed>().ETag.ShouldBe("W/\"0123abcd\"");
    }

    [Fact]
    public async Task WhenAnETagIsGiven_SendsItAsIfNoneMatch()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, TreeJson);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        await sut.ListRootDirectoryAsync(ValidBaseUrl, ValidSlug, "W/\"0123abcd\"", "token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.Headers.GetValues("If-None-Match").ShouldHaveSingleItem().ShouldBe("W/\"0123abcd\"");
    }

    [Fact]
    public async Task WhenNoETagIsGiven_SendsNoIfNoneMatch()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, TreeJson);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        await sut.ListRootDirectoryAsync(ValidBaseUrl, ValidSlug, ifNoneMatch: null, "token", CancellationToken.None);

        // Assert
        handler.LastRequest.ShouldNotBeNull().Headers.Contains("If-None-Match").ShouldBeFalse();
    }

    [Fact]
    public async Task WhenGitHubAnswersNotModified_ReportsUnchanged()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotModified, string.Empty);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<RootDirectoryListing> result = await sut.ListRootDirectoryAsync(
            ValidBaseUrl, ValidSlug, "W/\"0123abcd\"", "token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBeOfType<RootDirectoryListing.Unchanged>();
    }

    [Fact]
    public async Task WhenGitHubFails_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Conflict, """{ "message": "Git Repository is empty." }""");
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<RootDirectoryListing> result = await sut.ListRootDirectoryAsync(
            ValidBaseUrl, ValidSlug, ifNoneMatch: null, "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<RootDirectoryListing>.Failure>().Error.Message.ShouldContain("409");
    }

    [Fact]
    public async Task WhenBaseUrlIsNotHttps_ReturnsFailureWithoutACall()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, TreeJson);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<RootDirectoryListing> result = await sut.ListRootDirectoryAsync(
            new Uri("http://api.github.com"), ValidSlug, ifNoneMatch: null, "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<RootDirectoryListing>.Failure>().Error.ShouldBe(GitHubErrors.InvalidBaseUrl);
        handler.AllRequests.ShouldBeEmpty();
    }
}
