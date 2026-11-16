using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
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

public sealed class ReadBlobAsync
{
    private const string BlobSha = "a1e8f8d745cc87e3a9248358d9352bb7f9a0aeba";

    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug => RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient CreateSut(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenCalled_RequestsTheRawBlob()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "<configuration />");
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        await sut.ReadBlobAsync(ValidBaseUrl, ValidSlug, BlobSha, "token", CancellationToken.None);

        // Assert
        handler.LastRequest.ShouldNotBeNull().RequestUri.ShouldNotBeNull().AbsolutePath
            .ShouldBe($"/api/v4/projects/group%2Fproject/repository/blobs/{BlobSha}/raw");
    }

    [Fact]
    public async Task WhenGitLabReturnsTheBlob_ReturnsItsText()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "<configuration />");
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<string> result = await sut.ReadBlobAsync(ValidBaseUrl, ValidSlug, BlobSha, "token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe("<configuration />");
    }

    [Fact]
    public async Task WhenGitLabFails_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, string.Empty);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<string> result = await sut.ReadBlobAsync(ValidBaseUrl, ValidSlug, BlobSha, "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>().Error.Kind.ShouldBe(ErrorKind.NotFound);
    }
}
