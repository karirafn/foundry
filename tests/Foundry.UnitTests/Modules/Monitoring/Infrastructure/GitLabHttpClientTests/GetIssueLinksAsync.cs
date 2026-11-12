using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.TrackerReads;
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

public sealed class GetIssueLinksAsync
{
    private const string LinksPathSuffix = "/issues/28/links";

    // The project lookup returns the project_id the docs.gitlab.com/api/issue_links "List all issue links" example
    // carries (4).
    private const string ProjectLookup = """{"id":4,"default_branch":"main"}""";

    // Live capture from a private gitlab.com project, 2026-11-12, fields reduced to iid/link_type/state: the links
    // of a requested issue that is blocked by iid 8 and blocks iid 29, reported from the requested issue's side.
    // The capture carries no project_id, which the documented response does, so project_id 4 is added to each
    // element (derived).
    private const string CapturedLinks = """
        [{"iid":8,"project_id":4,"link_type":"is_blocked_by","state":"closed"},
         {"iid":29,"project_id":4,"link_type":"blocks","state":"opened"}]
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

    private static FakeHandler HandlerWithLinks(HttpStatusCode linksStatus, string linksBody)
    {
        return new FakeHandler(HttpStatusCode.OK, ProjectLookup)
            .WithRoute(LinksPathSuffix, linksStatus, linksBody);
    }

    private static Task<Result<IReadOnlyList<IssueReference>>> ReadLinksAsync(GitLabHttpClient sut)
    {
        return sut.GetIssueLinksAsync(ValidBaseUrl, Slug, issueNumber: 28, token: "glpat_token", CancellationToken.None);
    }

    [Fact]
    public async Task WhenTheIssueIsBlockedByOneAndBlocksAnother_MapsEachLinkTypeToItsRelationInResponseOrder()
    {
        // Arrange
        GitLabHttpClient sut = BuildSut(HandlerWithLinks(HttpStatusCode.OK, CapturedLinks));

        // Act
        Result<IReadOnlyList<IssueReference>> result = await ReadLinksAsync(sut);

        // Assert
        IReadOnlyList<IssueReference> references =
            result.ShouldBeOfType<Result<IReadOnlyList<IssueReference>>.Success>().Value;
        references.Select(reference => (reference.Number, reference.Relation)).ShouldBe(
        [
            (8, IssueRelation.BlockedBy),
            (29, IssueRelation.Blocks),
        ]);
    }

    [Fact]
    public async Task WhenALinkIsRelatedOrHasAnUndocumentedType_KeepsTheRelatedOneAndDropsTheOther()
    {
        // Arrange
        // The first element is the docs.gitlab.com/api/issue_links example reduced to the fields read. The second is
        // derived: the same element with an iid and a link_type GitLab does not document.
        string links = """
            [{"iid":14,"project_id":4,"state":"opened","link_type":"relates_to"},
             {"iid":15,"project_id":4,"state":"opened","link_type":"duplicates"}]
            """;
        GitLabHttpClient sut = BuildSut(HandlerWithLinks(HttpStatusCode.OK, links));

        // Act
        Result<IReadOnlyList<IssueReference>> result = await ReadLinksAsync(sut);

        // Assert
        IReadOnlyList<IssueReference> references =
            result.ShouldBeOfType<Result<IReadOnlyList<IssueReference>>.Success>().Value;
        references.Select(reference => (reference.Number, reference.Relation)).ShouldBe(
        [
            (14, IssueRelation.RelatesTo),
        ]);
    }

    [Fact]
    public async Task WhenALinkBelongsToAnotherProject_LeavesItOutAndDropsOneWithoutAProjectId()
    {
        // Arrange
        // Derived: the docs example element with project_id 5 (a project other than the lookup's 4) on the first
        // element and no project_id on the third; GitLab always sends project_id, so a missing one is not trusted
        // as same-project.
        string links = """
            [{"iid":14,"project_id":5,"state":"opened","link_type":"relates_to"},
             {"iid":8,"project_id":4,"state":"closed","link_type":"is_blocked_by"},
             {"iid":29,"state":"opened","link_type":"blocks"}]
            """;
        GitLabHttpClient sut = BuildSut(HandlerWithLinks(HttpStatusCode.OK, links));

        // Act
        Result<IReadOnlyList<IssueReference>> result = await ReadLinksAsync(sut);

        // Assert
        IReadOnlyList<IssueReference> references =
            result.ShouldBeOfType<Result<IReadOnlyList<IssueReference>>.Success>().Value;
        references.Select(reference => reference.Number).ShouldBe([8]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task WhenTheLinksCannotBeAccessed_ReturnsNoLinks(HttpStatusCode status)
    {
        // Arrange
        GitLabHttpClient sut = BuildSut(HandlerWithLinks(status, string.Empty));

        // Act
        Result<IReadOnlyList<IssueReference>> result = await ReadLinksAsync(sut);

        // Assert
        IReadOnlyList<IssueReference> references =
            result.ShouldBeOfType<Result<IReadOnlyList<IssueReference>>.Success>().Value;
        references.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheLinksCallFailsWithAServerError_FailsTheReadWithTheStatus()
    {
        // Arrange
        GitLabHttpClient sut = BuildSut(HandlerWithLinks(HttpStatusCode.InternalServerError, string.Empty));

        // Act
        Result<IReadOnlyList<IssueReference>> result = await ReadLinksAsync(sut);

        // Assert
        Error error = result.ShouldBeOfType<Result<IReadOnlyList<IssueReference>>.Failure>().Error;
        error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenTheProjectLookupFails_FailsTheReadWithoutReadingLinks()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<IssueReference>> result = await ReadLinksAsync(sut);

        // Assert
        result.ShouldBeOfType<Result<IReadOnlyList<IssueReference>>.Failure>();
        handler.AllRequests.ShouldNotContain(request => request.RequestUri!.AbsolutePath.EndsWith(LinksPathSuffix));
    }

    [Fact]
    public async Task WhenTheBaseUrlIsNotHttps_FailsWithoutCallingGitLab()
    {
        // Arrange
        FakeHandler handler = HandlerWithLinks(HttpStatusCode.OK, CapturedLinks);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<IssueReference>> result = await sut.GetIssueLinksAsync(
            new Uri("http://gitlab.com/api/v4"),
            Slug,
            issueNumber: 28,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Error error = result.ShouldBeOfType<Result<IReadOnlyList<IssueReference>>.Failure>().Error;
        error.Code.ShouldBe(GitLabErrors.InvalidBaseUrl.Code);
        handler.AllRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenReading_RequestsTheProjectThenTheLinksOfTheIssue()
    {
        // Arrange
        FakeHandler handler = HandlerWithLinks(HttpStatusCode.OK, CapturedLinks);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        await ReadLinksAsync(sut);

        // Assert
        handler.AllRequests.Select(request => request.RequestUri!.AbsolutePath).ShouldBe(
        [
            "/api/v4/projects/group%2Fproject",
            "/api/v4/projects/group%2Fproject/issues/28/links",
        ]);
    }
}
