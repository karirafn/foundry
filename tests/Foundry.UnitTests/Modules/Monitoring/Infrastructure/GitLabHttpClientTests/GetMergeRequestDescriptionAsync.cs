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

public sealed class GetMergeRequestDescriptionAsync
{
    private const string MergeRequestUrl = "https://gitlab.com/group/project/-/merge_requests/1";

    // Captured 2026-11-20 from GET projects/gitlab-org%2Fgitlab/merge_requests/1 on gitlab.com, cut to six fields and
    // the description to 60 characters.
    private const string Captured = """
        {"id":83382,"iid":1,"project_id":278964,"title":"Enhance the LDAP group synchronization","state":"merged","description":"The response of our company's LDAP for a group looks like th"}
        """;

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
    public async Task WhenCalled_RequestsTheMergeRequest()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, Captured);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        await sut.GetMergeRequestDescriptionAsync(ValidBaseUrl, ValidSlug, MergeRequestUrl, "token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.Method.ShouldBe(HttpMethod.Get);
        request.RequestUri.ShouldNotBeNull().AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/merge_requests/1");
    }

    [Fact]
    public async Task WhenTheMergeRequestHasADescription_ReturnsIt()
    {
        // Arrange
        GitLabHttpClient sut = CreateSut(new FakeHandler(HttpStatusCode.OK, Captured));

        // Act
        Result<string> result = await sut.GetMergeRequestDescriptionAsync(
            ValidBaseUrl, ValidSlug, MergeRequestUrl, "token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe("The response of our company's LDAP for a group looks like th");
    }

    [Fact]
    public async Task WhenTheDescriptionIsNull_ReturnsEmpty()
    {
        // Arrange
        // Derived from the captured response: only the description changes.
        string withNullDescription = Captured.Replace(
            "\"The response of our company's LDAP for a group looks like th\"", "null", StringComparison.Ordinal);
        GitLabHttpClient sut = CreateSut(new FakeHandler(HttpStatusCode.OK, withNullDescription));

        // Act
        Result<string> result = await sut.GetMergeRequestDescriptionAsync(
            ValidBaseUrl, ValidSlug, MergeRequestUrl, "token", CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenGitLabFails_ReturnsFailure()
    {
        // Arrange
        GitLabHttpClient sut = CreateSut(new FakeHandler(HttpStatusCode.NotFound, string.Empty));

        // Act
        Result<string> result = await sut.GetMergeRequestDescriptionAsync(
            ValidBaseUrl, ValidSlug, MergeRequestUrl, "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>().Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task WhenTheUrlIsNotAMergeRequest_FailsWithoutARequest()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, Captured);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<string> result = await sut.GetMergeRequestDescriptionAsync(
            ValidBaseUrl, ValidSlug, "https://gitlab.com/group/project/-/issues/3", "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>();
        handler.LastRequest.ShouldBeNull();
    }
}
