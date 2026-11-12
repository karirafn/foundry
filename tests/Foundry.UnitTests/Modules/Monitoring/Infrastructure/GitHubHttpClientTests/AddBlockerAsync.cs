using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;
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

public sealed class AddBlockerAsync
{
    private const string ValidToken = "ghp_token";
    private const int IssueNumber = 42;
    private const int BlockerNumber = 15;
    private const string BlockerPath = "/repos/owner/repo/issues/15";
    private const string DependenciesPath = "/repos/owner/repo/issues/42/dependencies/blocked_by";
    private const string BlockingPath = "/repos/owner/repo/issues/42/dependencies/blocking";

    // From the GitHub REST docs example response of "Get an issue" (an issue with "id": 1 and "number": 1347),
    // with the number changed to 15 and every other field cut.
    private const string BlockerIssueJson = """{ "id": 1, "number": 15, "state": "open" }""";

    // Derived from the example above; only the id differs, to pin that the id is read as an int64.
    private const string LargeIdBlockerIssueJson = """{ "id": 4294967297, "number": 15, "state": "open" }""";

    // From the GitHub REST docs example response of "Add a dependency an issue is blocked by"
    // (the blocking issue), reduced to the fields the client reads.
    private const string CreatedJson = """{ "id": 1, "number": 15, "state": "open" }""";

    // The documented 422 shape ("Validation failed" with an errors array and a documentation_url, as in GitHub's
    // "Resources in the REST API" validation-error docs); the field and error text are derived, since the
    // dependency endpoint documents no duplicate-link body.
    private const string ValidationFailedJson = """
        {"message":"Validation failed","errors":[{"resource":"Issue","code":"invalid","field":"issue_id","message":"Issue is already blocked by this issue"}],"documentation_url":"https://docs.github.com/rest/issues/issue-dependencies#add-a-dependency-an-issue-is-blocked-by"}
        """;

    // The dependency list shape the repo's GetIssueLinksAsync fixtures capture from
    // GET .../dependencies/blocked_by (fields read only), listing #15 as a blocker of #42.
    private const string RecordedBlockedByJson = """
        [{"number":15,"state":"open","title":"the blocker","repository":{"full_name":"owner/repo"}}]
        """;

    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug => RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(MethodRoutedFakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    private static Task<Result> AddAsync(GitHubHttpClient sut, Uri? baseUrl = null) =>
        sut.AddBlockerAsync(
            baseUrl ?? ValidBaseUrl, ValidSlug, IssueNumber, BlockerNumber, ValidToken, CancellationToken.None);

    [Fact]
    public async Task WhenTheBlockerExists_PostsItsIdToTheBlockedByEndpointAndSucceeds()
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(HttpMethod.Post, DependenciesPath, HttpStatusCode.Created, CreatedJson);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsSuccess.ShouldBeTrue(),
            () => handler.Requests.Select(request => $"{request.Method} {request.Path}")
                .ShouldBe([$"GET {BlockerPath}", $"POST {DependenciesPath}"]),
            () => handler.Requests[1].Body.ShouldBe("""{"issue_id":1}"""),
            () => handler.Requests[1].ApiVersion.ShouldBe("2026-03-10"),
            () => handler.Requests[1].Token.ShouldBe(ValidToken));
    }

    [Fact]
    public async Task WhenGitHubRejectsTheLinkAndTheBlockerIsNotRecorded_FailsPermanentWithGitHubsMessage()
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(HttpMethod.Post, DependenciesPath, HttpStatusCode.UnprocessableEntity, ValidationFailedJson)
            .Respond(HttpMethod.Get, DependenciesPath, HttpStatusCode.OK, "[]")
            .Respond(HttpMethod.Get, BlockingPath, HttpStatusCode.OK, "[]");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        Error error = result.ShouldBeOfType<Result.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(BlockerLinkErrors.PermanentCode),
            () => error.Message.ShouldContain("Validation failed"),
            () => error.Message.ShouldContain("Issue is already blocked by this issue"));
    }

    [Fact]
    public async Task WhenGitHubRejectsTheLinkAsDuplicateAndTheReadBackShowsTheBlocker_Succeeds()
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(HttpMethod.Post, DependenciesPath, HttpStatusCode.UnprocessableEntity, ValidationFailedJson)
            .Respond(HttpMethod.Get, DependenciesPath, HttpStatusCode.OK, RecordedBlockedByJson)
            .Respond(HttpMethod.Get, BlockingPath, HttpStatusCode.OK, "[]");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("<html>Unprocessable</html>")]
    [InlineData("")]
    [InlineData("""{"message":"Validation failed","errors":["issue is invalid"]}""")]
    [InlineData("""{"message":42,"errors":[{"message":7}]}""")]
    public async Task WhenGitHubsRejectionBodyIsNotTheDocumentedShape_FailsPermanentInsteadOfThrowing(string body)
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(HttpMethod.Post, DependenciesPath, HttpStatusCode.UnprocessableEntity, body)
            .Respond(HttpMethod.Get, DependenciesPath, HttpStatusCode.OK, "[]")
            .Respond(HttpMethod.Get, BlockingPath, HttpStatusCode.OK, "[]");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        Error error = result.ShouldBeOfType<Result.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(BlockerLinkErrors.PermanentCode),
            () => error.Message.ShouldNotBeNullOrWhiteSpace());
    }

    [Fact]
    public async Task WhenGitHubsRejectionBodyIsNotJson_NamesTheStatusInTheMessage()
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(HttpMethod.Post, DependenciesPath, HttpStatusCode.UnprocessableEntity, "<html>nope</html>")
            .Respond(HttpMethod.Get, DependenciesPath, HttpStatusCode.OK, "[]")
            .Respond(HttpMethod.Get, BlockingPath, HttpStatusCode.OK, "[]");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Message.ShouldContain("422");
    }

    [Theory]
    [InlineData("<html>bad gateway</html>")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"id":0,"number":15}""")]
    [InlineData("""{"id":"abc"}""")]
    public async Task WhenTheIdReadSucceedsWithoutAValidIssue_FailsPermanentAndMakesNoPost(string body)
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, body);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(BlockerLinkErrors.PermanentCode),
            () => handler.Requests.ShouldNotContain(request => request.Method == HttpMethod.Post));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "<html>forbidden</html>", "GitHub returned 403.")]
    [InlineData(HttpStatusCode.NotFound, "", "GitHub returned 404.")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "{}", "GitHub returned 422.")]
    public async Task WhenGitHubsRefusalCarriesNoMessage_NamesOnlyTheStatusWithoutRestatingTheLink(
        HttpStatusCode status,
        string body,
        string expectedMessage)
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(HttpMethod.Post, DependenciesPath, status, body)
            .Respond(HttpMethod.Get, DependenciesPath, HttpStatusCode.OK, "[]")
            .Respond(HttpMethod.Get, BlockingPath, HttpStatusCode.OK, "[]");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Message.ShouldBe(expectedMessage);
    }

    [Fact]
    public async Task WhenGitHubRejectsAsDuplicateWithANonJsonBodyAndTheReadBackShowsTheBlocker_Succeeds()
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(HttpMethod.Post, DependenciesPath, HttpStatusCode.UnprocessableEntity, "<html>nope</html>")
            .Respond(HttpMethod.Get, DependenciesPath, HttpStatusCode.OK, RecordedBlockedByJson)
            .Respond(HttpMethod.Get, BlockingPath, HttpStatusCode.OK, "[]");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenGitHubsMessageIsLongAndMultiLine_FlattensItToOneLineCappedAt500Characters()
    {
        // Arrange
        string longMessage = $$"""{"message":"first line\nsecond line {{new string('x', 1000)}}"}""";
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(HttpMethod.Post, DependenciesPath, HttpStatusCode.UnprocessableEntity, longMessage)
            .Respond(HttpMethod.Get, DependenciesPath, HttpStatusCode.OK, "[]")
            .Respond(HttpMethod.Get, BlockingPath, HttpStatusCode.OK, "[]");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        string message = result.ShouldBeOfType<Result.Failure>().Error.Message;
        message.ShouldSatisfyAllConditions(
            () => message.Length.ShouldBe(500),
            () => message.ShouldStartWith("first line second line "),
            () => message.ShouldNotContain('\n'));
    }

    [Fact]
    public async Task WhenGitHubsMessageIsCutInsideASurrogatePair_DropsTheWholePair()
    {
        // Arrange
        string emoji = char.ConvertFromUtf32(0x1F600);
        string body = $$"""{"message":"{{new string('a', 498)}}{{emoji}}{{new string('b', 10)}}"}""";
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(HttpMethod.Post, DependenciesPath, HttpStatusCode.UnprocessableEntity, body)
            .Respond(HttpMethod.Get, DependenciesPath, HttpStatusCode.OK, "[]")
            .Respond(HttpMethod.Get, BlockingPath, HttpStatusCode.OK, "[]");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Message.ShouldBe($"{new string('a', 498)}…");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task WhenThePostAnswersARetryableStatus_FailsTransient(HttpStatusCode status)
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(HttpMethod.Post, DependenciesPath, status, "{}");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        BlockerLinkErrors.IsTransient(result.ShouldBeOfType<Result.Failure>().Error).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenThePostIsForbiddenByTheRateLimit_FailsTransient()
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(HttpMethod.Post, DependenciesPath, HttpStatusCode.Forbidden, """{"message":"API rate limit exceeded"}""");
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "0";
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        BlockerLinkErrors.IsTransient(result.ShouldBeOfType<Result.Failure>().Error).ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task WhenThePostAnswersNotFoundOrGone_FailsPermanent(HttpStatusCode status)
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(HttpMethod.Post, DependenciesPath, status, """{"message":"Not Found"}""");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(BlockerLinkErrors.PermanentCode);
    }

    [Fact]
    public async Task WhenGitHubsRejectionHasAnErrorDetailButNoMessage_StartsTheMessageWithTheDetail()
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(
                HttpMethod.Post,
                DependenciesPath,
                HttpStatusCode.UnprocessableEntity,
                """{"errors":[{"message":"Issue is already blocked by this issue"}]}""")
            .Respond(HttpMethod.Get, DependenciesPath, HttpStatusCode.OK, "[]")
            .Respond(HttpMethod.Get, BlockingPath, HttpStatusCode.OK, "[]");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Message.ShouldBe("Issue is already blocked by this issue");
    }

    [Fact]
    public async Task WhenThePostIsRefusedWithAMessage_FailsPermanentWithGitHubsMessageAlone()
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(
                HttpMethod.Post,
                DependenciesPath,
                HttpStatusCode.Forbidden,
                """{"message":"Resource not accessible by integration"}""");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Message
            .ShouldBe("Resource not accessible by integration");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, BlockerLinkErrors.PermanentCode)]
    [InlineData(HttpStatusCode.Gone, BlockerLinkErrors.PermanentCode)]
    [InlineData(HttpStatusCode.InternalServerError, BlockerLinkErrors.TransientCode)]
    public async Task WhenTheIdReadFails_ClassifiesTheStatusAndMakesNoPost(HttpStatusCode status, string expectedCode)
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, status, """{"message":"Not Found"}""");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(expectedCode),
            () => handler.Requests.ShouldNotContain(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task WhenTheIdReadCannotConnect_FailsTransient()
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler().FailToConnect(HttpMethod.Get, BlockerPath);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        BlockerLinkErrors.IsTransient(result.ShouldBeOfType<Result.Failure>().Error).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenThePostCannotConnect_FailsTransient()
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .FailToConnect(HttpMethod.Post, DependenciesPath);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        BlockerLinkErrors.IsTransient(result.ShouldBeOfType<Result.Failure>().Error).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTheBaseUrlIsNotHttps_FailsPermanentWithoutACall()
    {
        // Arrange
        MethodRoutedFakeHandler handler = new();
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut, new Uri("http://api.github.com"));

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(BlockerLinkErrors.PermanentCode),
            () => handler.Requests.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenTheReadBackAfterARejectionFails_FailsTransient()
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(HttpMethod.Post, DependenciesPath, HttpStatusCode.UnprocessableEntity, ValidationFailedJson)
            .Respond(HttpMethod.Get, DependenciesPath, HttpStatusCode.InternalServerError, "{}");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        BlockerLinkErrors.IsTransient(result.ShouldBeOfType<Result.Failure>().Error).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTheReadBackCannotConnect_FailsTransient()
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, BlockerIssueJson)
            .Respond(HttpMethod.Post, DependenciesPath, HttpStatusCode.UnprocessableEntity, ValidationFailedJson)
            .FailToConnect(HttpMethod.Get, DependenciesPath);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result result = await AddAsync(sut);

        // Assert
        BlockerLinkErrors.IsTransient(result.ShouldBeOfType<Result.Failure>().Error).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTheBlockersIdExceedsInt32_PostsItWhole()
    {
        // Arrange
        MethodRoutedFakeHandler handler = new MethodRoutedFakeHandler()
            .Respond(HttpMethod.Get, BlockerPath, HttpStatusCode.OK, LargeIdBlockerIssueJson)
            .Respond(HttpMethod.Post, DependenciesPath, HttpStatusCode.Created, CreatedJson);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await AddAsync(sut);

        // Assert
        handler.Requests[1].Body.ShouldBe("""{"issue_id":4294967297}""");
    }
}
