using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetIssueStatusAsync
{
    // docs.gitlab.com/api/issues "Retrieve a project issue" example response, reduced to the fields read: the docs
    // example is a closed issue with iid 1.
    private const string DocsIssue = """
        {"iid":1,"title":"Ut commodi ullam eos dolores perferendis nihil sunt.","description":"Omnis vero earum sunt corporis dolor et placeat.","state":"closed","labels":[]}
        """;

    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug Slug => RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient BuildSut(FakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenTheIssueIsRead_ReturnsItsStateAndDescription()
    {
        // Arrange
        GitLabHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, DocsIssue));

        // Act
        Result<IssueStatus> result = await sut.GetIssueStatusAsync(
            ValidBaseUrl, Slug, issueNumber: 1, token: "token", CancellationToken.None);

        // Assert
        IssueStatus status = result.ShouldBeOfType<Result<IssueStatus>.Success>().Value;
        status.ShouldBe(new IssueStatus(IsClosed: true, Body: "Omnis vero earum sunt corporis dolor et placeat."));
    }

    [Fact]
    public async Task WhenTheDescriptionIsNull_ReturnsTheEmptyBody()
    {
        // Arrange
        string json = """{"iid":1,"title":"Add retry","description":null,"state":"opened","labels":[]}""";
        GitLabHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, json));

        // Act
        Result<IssueStatus> result = await sut.GetIssueStatusAsync(
            ValidBaseUrl, Slug, issueNumber: 1, token: "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueStatus>.Success>().Value
            .ShouldBe(new IssueStatus(IsClosed: false, Body: string.Empty));
    }

    [Fact]
    public async Task WhenReading_SendsTheIssueRequestIsIssueClosedSends()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, DocsIssue);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetIssueStatusAsync(ValidBaseUrl, Slug, issueNumber: 1, token: "token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.AllRequests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.RequestUri.ShouldNotBeNull().AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/issues/1");
    }

    [Fact]
    public async Task WhenGitLabAnswersWithAServerError_Fails()
    {
        // Arrange
        GitLabHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.InternalServerError, string.Empty));

        // Act
        Result<IssueStatus> result = await sut.GetIssueStatusAsync(
            ValidBaseUrl, Slug, issueNumber: 1, token: "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueStatus>.Failure>().Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenTheBaseUrlIsNotHttps_FailsWithoutARequest()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, DocsIssue);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueStatus> result = await sut.GetIssueStatusAsync(
            new Uri("ftp://gitlab.com/api/v4"), Slug, issueNumber: 1, token: "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueStatus>.Failure>().Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
        handler.AllRequests.ShouldBeEmpty();
    }
}
