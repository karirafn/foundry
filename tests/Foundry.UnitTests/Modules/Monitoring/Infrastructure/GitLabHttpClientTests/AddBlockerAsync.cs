using System.Net;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;
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

public sealed class AddBlockerAsync
{
    private const int IssueNumber = 42;
    private const int BlockerNumber = 15;

    // The project lookup returns the project_id the docs.gitlab.com/api/issue_links examples carry (4).
    private const string ProjectLookup = """{"id":4,"default_branch":"main"}""";

    // The docs.gitlab.com/api/issue_links "Create an issue link" response, reduced to link_type (the other
    // fields are not read) with link_type is_blocked_by (derived: the docs example shows relates_to).
    private const string CreatedLink = """{"source_issue":{"iid":42},"target_issue":{"iid":15},"link_type":"is_blocked_by"}""";

    // Live capture from a private gitlab.com project, 2026-11-12, fields reduced to iid/link_type/state: the
    // links of a requested issue, reported from the requested issue's side. The capture carries no project_id,
    // which the documented response does, so project_id 4 is added to each element, and the iids are replaced
    // by 15 and 29 (derived).
    private const string BlockedByLinks = """
        [{"iid":15,"project_id":4,"link_type":"is_blocked_by","state":"opened"},
         {"iid":29,"project_id":4,"link_type":"blocks","state":"opened"}]
        """;

    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug Slug => RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient BuildSut(DelegatingHandler handler)
    {
        return new GitLabHttpClient(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);
    }

    private static Task<Result> AddAsync(GitLabHttpClient sut)
    {
        return sut.AddBlockerAsync(
            ValidBaseUrl, Slug, IssueNumber, BlockerNumber, token: "glpat_token", CancellationToken.None);
    }

    [Fact]
    public async Task WhenGitLabRecordsTheLinkAndTheReadBackShowsTheBlocker_PostsTheBlockedByLinkAndSucceeds()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, ProjectLookup),
            (HttpStatusCode.Created, CreatedLink),
            (HttpStatusCode.OK, BlockedByLinks),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        handler.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Success>(),
            () => handler.Requests[1].Method.ShouldBe(HttpMethod.Post),
            () => handler.Requests[1].RequestUri!.AbsoluteUri
                .ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/issues/42/links"),
            () => handler.RequestBodies[1].ShouldBe(
                """{"target_project_id":4,"target_issue_iid":15,"link_type":"is_blocked_by"}"""));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task WhenGitLabAnswersWithAServerErrorOrRateLimit_FailsTransientlyWithoutReadingBack(
        HttpStatusCode status)
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, ProjectLookup),
            (status, string.Empty),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        handler.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(BlockerLinkErrors.TransientCode),
            () => handler.Requests.Count.ShouldBe(2));
    }

    [Theory]
    [InlineData("group%2Fproject/issues/42/links")]
    [InlineData("projects/group%2Fproject")]
    public async Task WhenTheConnectionFails_FailsTransiently(string throwingPath)
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, ProjectLookup)
            .WithThrowingRoute(throwingPath, () => new HttpRequestException("connection reset"));
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(BlockerLinkErrors.TransientCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, """{"message":"403 Forbidden"}""", "403 Forbidden")]
    [InlineData(HttpStatusCode.NotFound, """{"message":"404 Not found"}""", "404 Not found")]
    public async Task WhenGitLabRefusesTheLinkWithAClientError_FailsPermanentlyWithGitLabsMessageWithoutReadingBack(
        HttpStatusCode status,
        string body,
        string expectedMessage)
    {
        // Arrange
        // The bodies are the {"message": "<status> <reason>"} shape GitLab's API returns for these statuses.
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, ProjectLookup),
            (status, body),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        Error error = result.ShouldBeOfType<Result.Failure>().Error;
        handler.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(BlockerLinkErrors.PermanentCode),
            () => error.Message.ShouldContain(expectedMessage),
            () => handler.Requests.Count.ShouldBe(2));
    }

    [Theory]
    [InlineData("""{"message":["link_type is invalid","target is invalid"]}""", "link_type is invalid")]
    [InlineData("""{"message":{"link_type":["is invalid"]}}""", "link_type")]
    [InlineData("<html>Bad Gateway</html>", "")]
    [InlineData("", "")]
    public async Task WhenGitLabsMessageIsAnArrayAnObjectOrNotJson_FailsPermanentlyInsteadOfThrowing(
        string body,
        string expectedFragment)
    {
        // Arrange
        // Derived: GitLab validation errors carry message as an array or as an object keyed by attribute; a proxy
        // can answer with a body that is not JSON at all.
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, ProjectLookup),
            (HttpStatusCode.UnprocessableEntity, body),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        Error error = result.ShouldBeOfType<Result.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(BlockerLinkErrors.PermanentCode),
            () => error.Message.ShouldContain("422"),
            () => error.Message.ShouldContain(expectedFragment));
    }

    [Fact]
    public async Task WhenGitLabsMessageIsLong_CapsTheProviderMessageAt500Characters()
    {
        // Arrange
        string longMessage = new('x', 2000);
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, ProjectLookup),
            (HttpStatusCode.UnprocessableEntity, $$"""{"message":"{{longMessage}}"}"""),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        string message = result.ShouldBeOfType<Result.Failure>().Error.Message;
        message.ShouldSatisfyAllConditions(
            () => message.ShouldEndWith($"{new string('x', 499)}…"),
            () => message.ShouldNotContain(new string('x', 500)));
    }

    [Fact]
    public async Task WhenGitLabsMessageIsCutInsideASurrogatePair_DropsTheWholePair()
    {
        // Arrange
        string emoji = char.ConvertFromUtf32(0x1F600);
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, ProjectLookup),
            (
                HttpStatusCode.UnprocessableEntity,
                $$"""{"message":"{{new string('a', 498)}}{{emoji}}{{new string('b', 10)}}"}"""),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Message.ShouldEndWith($"{new string('a', 498)}…");
    }

    [Fact]
    public async Task WhenReadingTheLinksBack_RequestsTheLargestPageSoALongLinkListStillShowsTheBlocker()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, ProjectLookup),
            (HttpStatusCode.Created, CreatedLink),
            (HttpStatusCode.OK, BlockedByLinks),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        await AddAsync(sut);

        // Assert
        handler.Requests[2].RequestUri!.AbsoluteUri
            .ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/issues/42/links?per_page=100");
    }

    [Fact]
    public async Task WhenTheReadBackFails_FailsTransientlySoARetryCanConfirmTheLink()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, ProjectLookup),
            (HttpStatusCode.Created, CreatedLink),
            (HttpStatusCode.ServiceUnavailable, string.Empty),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(BlockerLinkErrors.TransientCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, BlockerLinkErrors.TransientCode)]
    [InlineData(HttpStatusCode.TooManyRequests, BlockerLinkErrors.TransientCode)]
    [InlineData(HttpStatusCode.NotFound, BlockerLinkErrors.PermanentCode)]
    [InlineData(HttpStatusCode.Forbidden, BlockerLinkErrors.PermanentCode)]
    public async Task WhenTheProjectLookupFails_ClassifiesTheStatusAndMakesNoLinkCall(
        HttpStatusCode status,
        string expectedCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(status, string.Empty)]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        handler.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(expectedCode),
            () => handler.Requests.Count.ShouldBe(1));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, BlockerLinkErrors.PermanentCode)]
    [InlineData(HttpStatusCode.Forbidden, BlockerLinkErrors.PermanentCode)]
    [InlineData(HttpStatusCode.TooManyRequests, BlockerLinkErrors.TransientCode)]
    public async Task WhenTheReadBackFailsWithAClientStatus_ClassifiesTheStatus(
        HttpStatusCode status,
        string expectedCode)
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, ProjectLookup),
            (HttpStatusCode.Created, CreatedLink),
            (status, string.Empty),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(expectedCode);
    }

    [Fact]
    public async Task WhenTheProjectLookupAnswersWithInvalidJson_ThrowsInsteadOfReportingATransientFailure()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, "<html>proxy</html>")]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Task<Result> act = AddAsync(sut);

        // Assert
        await Should.ThrowAsync<JsonException>(act);
    }

    [Fact]
    public async Task WhenTheBaseUrlIsNotHttps_FailsPermanentlyWithoutCallingGitLab()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, ProjectLookup);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result result = await sut.AddBlockerAsync(
            new Uri("http://gitlab.com/api/v4"),
            Slug,
            IssueNumber,
            BlockerNumber,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        handler.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(BlockerLinkErrors.PermanentCode),
            () => handler.AllRequests.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenGitLabReportsTheLinkAsAlreadyThereAndTheReadBackShowsTheBlocker_Succeeds()
    {
        // Arrange
        // Derived: GitLab's IssuableLinks::CreateService answers 409 with the message "Issue(s) already assigned"
        // when every referenced issue is already linked.
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, ProjectLookup),
            (HttpStatusCode.Conflict, """{"message":"Issue(s) already assigned"}"""),
            (HttpStatusCode.OK, BlockedByLinks),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.ShouldBeOfType<Result.Success>();
    }

    [Fact]
    public async Task WhenGitLabReportsTheIssuesAsAlreadyLinkedWithoutABlockingLink_FailsPermanentlyWithGitLabsMessage()
    {
        // Arrange
        // The 409 body is derived as in the test above; the read-back is the docs.gitlab.com/api/issue_links list
        // example element reduced to the fields read, with iid 15 (the issues are already related).
        string relatedOnly = """[{"iid":15,"project_id":4,"state":"opened","link_type":"relates_to"}]""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, ProjectLookup),
            (HttpStatusCode.Conflict, """{"message":"Issue(s) already assigned"}"""),
            (HttpStatusCode.OK, relatedOnly),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        Error error = result.ShouldBeOfType<Result.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(BlockerLinkErrors.PermanentCode),
            () => error.Message.ShouldContain("Issue(s) already assigned"));
    }

    [Fact]
    public async Task WhenGitLabAcceptsTheLinkButTheReadBackLacksTheBlocker_FailsPermanentlyNamingThePremiumRequirement()
    {
        // Arrange
        // Derived: the read-back of a GitLab Free project, which answers 2xx but stores the link as relates_to
        // (the docs.gitlab.com/api/issue_links list example element, with iid 15).
        string relatedOnly = """[{"iid":15,"project_id":4,"state":"opened","link_type":"relates_to"}]""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, ProjectLookup),
            (HttpStatusCode.Created, CreatedLink),
            (HttpStatusCode.OK, relatedOnly),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        Error error = result.ShouldBeOfType<Result.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(BlockerLinkErrors.PermanentCode),
            () => error.Message.ShouldBe(
                "GitLab did not record #15 as blocking #42; blocking links need GitLab Premium or Ultimate"));
    }
}
