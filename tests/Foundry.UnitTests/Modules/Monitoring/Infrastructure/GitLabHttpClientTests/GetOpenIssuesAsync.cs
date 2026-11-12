using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
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

public sealed class GetOpenIssuesAsync
{
    // docs.gitlab.com/api/issues, "List project issues" example, reduced to the fields read, followed by a second
    // element that is derived: the same shape with another iid and title, to show the response order is kept.
    private const string DocsExampleWithADerivedSecondIssue = """
        [{"iid":1,"project_id":4,"title":"Ut commodi ullam eos dolores perferendis nihil sunt.","state":"closed","labels":["foo","bar"]},
         {"iid":2,"project_id":4,"title":"Second open issue","state":"opened","labels":[]}]
        """;

    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug Slug => RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient BuildSut(FakeHandler handler, InMemoryProviderRateBudget? budget = null)
    {
        HttpClient httpClient = new(handler);
        return new GitLabHttpClient(
            httpClient,
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            budget ?? new InMemoryProviderRateBudget(),
            TimeProvider.System);
    }

    private static Task<Result<IReadOnlyList<OpenIssue>>> ReadIndexAsync(GitLabHttpClient sut)
    {
        return sut.GetOpenIssuesAsync(ValidBaseUrl, Slug, token: "glpat_token", CancellationToken.None);
    }

    [Fact]
    public async Task WhenGitLabListsOpenIssues_ReturnsTheirIidsAndTitlesInResponseOrder()
    {
        // Arrange
        GitLabHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, DocsExampleWithADerivedSecondIssue));

        // Act
        Result<IReadOnlyList<OpenIssue>> result = await ReadIndexAsync(sut);

        // Assert
        IReadOnlyList<OpenIssue> issues = result.ShouldBeOfType<Result<IReadOnlyList<OpenIssue>>.Success>().Value;
        issues.Select(issue => (issue.Number, issue.Title)).ShouldBe(
        [
            (1, "Ut commodi ullam eos dolores perferendis nihil sunt."),
            (2, "Second open issue"),
        ]);
    }

    [Fact]
    public async Task WhenReading_RequestsOnlyNonConfidentialIssuesNewestFirstWithoutALabelFilter()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, DocsExampleWithADerivedSecondIssue);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        await ReadIndexAsync(sut);

        // Assert
        // confidential is a boolean filter on "List project issues" (docs.gitlab.com/api/issues); false keeps
        // confidential issues out of a run whose output is posted publicly.
        handler.AllRequests.Select(request => request.RequestUri!.PathAndQuery).ShouldBe(
        [
            "/api/v4/projects/group%2Fproject/issues?state=opened&confidential=false&order_by=created_at&sort=desc&per_page=100",
        ]);
    }

    [Fact]
    public async Task WhenGitLabAnswersWithAServerError_FailsTheReadWithTheStatus()
    {
        // Arrange
        GitLabHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.InternalServerError, string.Empty));

        // Act
        Result<IReadOnlyList<OpenIssue>> result = await ReadIndexAsync(sut);

        // Assert
        Error error = result.ShouldBeOfType<Result<IReadOnlyList<OpenIssue>>.Failure>().Error;
        error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenTheBaseUrlIsNotHttps_FailsWithoutCallingGitLab()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, DocsExampleWithADerivedSecondIssue);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<OpenIssue>> result = await sut.GetOpenIssuesAsync(
            new Uri("http://gitlab.com/api/v4"),
            Slug,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Error error = result.ShouldBeOfType<Result<IReadOnlyList<OpenIssue>>.Failure>().Error;
        error.Code.ShouldBe(GitLabErrors.InvalidBaseUrl.Code);
        handler.AllRequests.ShouldBeEmpty();
    }
}
