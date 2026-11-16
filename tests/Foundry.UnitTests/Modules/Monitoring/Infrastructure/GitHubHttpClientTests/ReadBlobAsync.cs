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

public sealed class ReadBlobAsync
{
    private const string BlobSha = "44b4fc6d56897b048c772eb4087f854f46256132";

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
    public async Task WhenCalled_RequestsTheRawBlob()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "<configuration />");
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        await sut.ReadBlobAsync(ValidBaseUrl, ValidSlug, BlobSha, "token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull().AbsolutePath.ShouldBe($"/repos/owner/repo/git/blobs/{BlobSha}");
        request.Headers.Accept.ShouldHaveSingleItem().MediaType.ShouldBe("application/vnd.github.raw+json");
    }

    [Fact]
    public async Task WhenGitHubReturnsTheBlob_ReturnsItsText()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "<configuration />");
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<string> result = await sut.ReadBlobAsync(ValidBaseUrl, ValidSlug, BlobSha, "token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe("<configuration />");
    }

    [Fact]
    public async Task WhenGitHubFails_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, string.Empty);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<string> result = await sut.ReadBlobAsync(ValidBaseUrl, ValidSlug, BlobSha, "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>().Error.Kind.ShouldBe(ErrorKind.NotFound);
    }
}
