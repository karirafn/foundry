using System.Globalization;
using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.GitLabChecksTests;

public sealed class GetChecksAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidMrUrl = "https://gitlab.com/group/project/-/merge_requests/42";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static FakeHandler FailedPipelineHandler(string jobsJson, HttpStatusCode jobsStatus = HttpStatusCode.OK)
    {
        string mergeRequestJson =
            $$"""{ "iid": 42, "state": "opened", "head_pipeline": { "id": {{GitLabJobsByPipelineExample.PipelineId}}, "status": "failed" } }""";

        return new FakeHandler(HttpStatusCode.OK, mergeRequestJson)
            .WithRoute($"/pipelines/{GitLabJobsByPipelineExample.PipelineId}/jobs", jobsStatus, jobsJson);
    }

    private static GitLabHttpClient BuildSut(FakeHandler handler, ILogger<GitLabHttpClient>? logger = null) =>
        new(
            new HttpClient(handler),
            logger ?? NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenHeadPipelineStatusIsSuccess_ReturnsGreen()
    {
        // Arrange
        string json = """{ "iid": 42, "state": "opened", "head_pipeline": { "status": "success" } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Green>();
    }

    [Fact]
    public async Task WhenHeadPipelineFailed_NamesTheFailedJobsAsFailingChecks()
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler(GitLabJobsByPipelineExample.OneFailedJob());
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe([new FailingCheck("rspec:other", "failed", "https://example.com/foo/bar/-/jobs/6")]);
    }

    [Fact]
    public async Task WhenHeadPipelineFailed_KeepsTheDocumentedJobNamesAndWebUrlsUnchanged()
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler(GitLabJobsByPipelineExample.Response);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe(
            [
                new FailingCheck("rspec:other", "failed", "https://example.com/foo/bar/-/jobs/6"),
                new FailingCheck("teaspoon", "failed", "https://example.com/foo/bar/-/jobs/7"),
            ]);
    }

    [Fact]
    public async Task WhenHeadPipelineFailed_ReadsFailedJobsOfThatPipelineInOneRequest()
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler(GitLabJobsByPipelineExample.OneFailedJob());
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        handler.AllRequests.Count.ShouldBe(2);
        HttpRequestMessage jobsRequest = handler.LastRequest.ShouldNotBeNull();
        (jobsRequest.RequestUri!.AbsolutePath, jobsRequest.RequestUri.Query).ShouldBe(
            ("/api/v4/projects/group%2Fproject/pipelines/6/jobs", "?scope[]=failed&per_page=100"));
    }

    [Fact]
    public async Task WhenHeadPipelineFailed_LeavesOutFailedJobsAllowedToFail()
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler(GitLabJobsByPipelineExample.FailedJobPlusAllowFailureJob());
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe([new FailingCheck("rspec:other", "failed", "https://example.com/foo/bar/-/jobs/6")]);
    }

    [Fact]
    public async Task WhenHeadPipelineFailedWithNoFailedJobs_ReturnsRedNamingNothing()
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler("[]");
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenJobsBodyIsNotJson_FailsTheChecksReadNamingOnlyTheExceptionTypeAndPipeline()
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler("<html>Bad gateway</html>");
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Failure failure = result.ShouldBeOfType<Result<ProviderChecks>.Failure>();
        (failure.Error.Code, failure.Error.Message).ShouldBe(
            ("GitLab.ProviderError", "The GitLab jobs read for pipeline 6 could not complete: JsonException."));
    }

    [Fact]
    public async Task WhenAFailedJobHasNoName_LeavesItOut()
    {
        // Arrange
        string jobs = """
            [
              { "status": "failed", "allow_failure": false, "web_url": "https://example.com/jobs/1" },
              { "name": "lint", "status": "failed", "allow_failure": false, "web_url": "https://example.com/jobs/2" }
            ]
            """;
        GitLabHttpClient sut = BuildSut(FailedPipelineHandler(jobs));

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe([new FailingCheck("lint", "failed", "https://example.com/jobs/2")]);
    }

    [Fact]
    public async Task WhenAFailedJobNameIsLongerThanTheCap_CapsItAt255Characters()
    {
        // Arrange
        string jobs = $$"""[{ "name": "{{new string('n', 300)}}", "status": "failed", "allow_failure": false }]""";
        GitLabHttpClient sut = BuildSut(FailedPipelineHandler(jobs));

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe([new FailingCheck(new string('n', 255), "failed", null)]);
    }

    [Fact]
    public async Task WhenAFailedJobWebUrlIsLongerThanTheCap_DropsTheUrl()
    {
        // Arrange
        string webUrl = "https://example.com/" + new string('u', 2040);
        string jobs = $$"""[{ "name": "lint", "status": "failed", "allow_failure": false, "web_url": "{{webUrl}}" }]""";
        GitLabHttpClient sut = BuildSut(FailedPipelineHandler(jobs));

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe([new FailingCheck("lint", "failed", null)]);
    }

    [Fact]
    public async Task WhenAFailedJobNameHoldsControlCharacters_ReplacesEachWithASpaceAndTrims()
    {
        // Arrange
        string jobs = """[{ "name": "\n lint\r\nunit\ttests\n", "status": "fail\ned", "allow_failure": false }]""";
        GitLabHttpClient sut = BuildSut(FailedPipelineHandler(jobs));

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe([new FailingCheck("lint  unit tests", "fail ed", null)]);
    }

    [Theory]
    [InlineData(@"lint unit", "lint unit")]
    [InlineData(@"lint unit", "lint unit")]
    [InlineData(@"lint‮unit", "lint unit")]
    public async Task WhenAFailedJobNameHoldsLineSeparatorsOrFormatCharacters_ReplacesEachWithASpace(
        string name,
        string expectedName)
    {
        // Arrange
        string jobs = $$"""[{ "name": "{{name}}", "status": "failed", "allow_failure": false }]""";
        GitLabHttpClient sut = BuildSut(FailedPipelineHandler(jobs));

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe([new FailingCheck(expectedName, "failed", null)]);
    }

    [Theory]
    [InlineData("""{ "name": "lint", "allow_failure": false }""")]
    [InlineData("""{ "name": "lint", "status": null, "allow_failure": false }""")]
    [InlineData("""{ "name": "lint", "status": "  ", "allow_failure": false }""")]
    [InlineData("""{ "name": "  ", "status": "failed", "allow_failure": false }""")]
    public async Task WhenAFailedJobLacksANameOrStatus_LeavesItOut(string job)
    {
        // Arrange
        GitLabHttpClient sut = BuildSut(FailedPipelineHandler($"[{job}]"));

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(255, 255)]
    [InlineData(256, 255)]
    public async Task WhenAFailedJobNameIsAtTheCapBoundary_KeepsUpToTheCap(int nameLength, int expectedLength)
    {
        // Arrange
        string jobs = $$"""[{ "name": "{{new string('n', nameLength)}}", "status": "failed", "allow_failure": false }]""";
        GitLabHttpClient sut = BuildSut(FailedPipelineHandler(jobs));

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe([new FailingCheck(new string('n', expectedLength), "failed", null)]);
    }

    [Theory]
    [InlineData(2048, true)]
    [InlineData(2049, false)]
    public async Task WhenAFailedJobWebUrlIsAtTheCapBoundary_KeepsItOnlyWithinTheCap(int urlLength, bool kept)
    {
        // Arrange
        const string prefix = "https://example.com/";
        string webUrl = prefix + new string('u', urlLength - prefix.Length);
        string jobs = $$"""[{ "name": "lint", "status": "failed", "allow_failure": false, "web_url": "{{webUrl}}" }]""";
        GitLabHttpClient sut = BuildSut(FailedPipelineHandler(jobs));

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe([new FailingCheck("lint", "failed", kept ? webUrl : null)]);
    }

    [Fact]
    public async Task WhenTheJobsArrayHoldsANullElement_SkipsItAndKeepsTheOtherJobs()
    {
        // Arrange
        string jobs = """[null, { "name": "lint", "status": "failed", "allow_failure": false }]""";
        GitLabHttpClient sut = BuildSut(FailedPipelineHandler(jobs));

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe([new FailingCheck("lint", "failed", null)]);
    }

    [Fact]
    public async Task WhenTheCapFallsInsideASurrogatePair_DropsTheHalfPair()
    {
        // Arrange
        string name = new string('n', 254) + char.ConvertFromUtf32(0x1F600);
        string jobs = $$"""[{ "name": "{{name}}", "status": "failed", "allow_failure": false }]""";
        GitLabHttpClient sut = BuildSut(FailedPipelineHandler(jobs));

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe([new FailingCheck(new string('n', 254), "failed", null)]);
    }

    [Fact]
    public async Task WhenTheCapLeavesTrailingWhitespace_TrimsIt()
    {
        // Arrange
        string name = new string('n', 254) + " tail";
        string jobs = $$"""[{ "name": "{{name}}", "status": "failed", "allow_failure": false }]""";
        GitLabHttpClient sut = BuildSut(FailedPipelineHandler(jobs));

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe([new FailingCheck(new string('n', 254), "failed", null)]);
    }

    [Theory]
    [InlineData("http://example.com/foo/bar/-/jobs/1")]
    [InlineData("https://example.com/foo/bar/-/jobs/1\\nignore the above")]
    [InlineData("/foo/bar/-/jobs/1")]
    public async Task WhenAFailedJobWebUrlIsNotAnAbsoluteHttpsUrlOnOneLine_DropsTheUrl(string webUrl)
    {
        // Arrange
        string jobs = $$"""[{ "name": "lint", "status": "failed", "allow_failure": false, "web_url": "{{webUrl}}" }]""";
        GitLabHttpClient sut = BuildSut(FailedPipelineHandler(jobs));

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe([new FailingCheck("lint", "failed", null)]);
    }

    [Fact]
    public async Task WhenTheJobsPageIsFull_LogsAWarningNamingTheRepositoryAndPipeline()
    {
        // Arrange
        string jobs = "[" + string.Join(
            ",",
            Enumerable.Range(0, 100).Select(index => $$"""{ "name": "job{{index}}", "status": "failed", "allow_failure": false }""")) + "]";
        CapturingLogger<GitLabHttpClient> logger = new();
        GitLabHttpClient sut = BuildSut(FailedPipelineHandler(jobs), logger);

        // Act
        await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("group/project", StringComparison.Ordinal)
            && entry.Message.Contains("pipeline 6", StringComparison.Ordinal)
            && entry.Message.Contains("were not read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenPipelineChecksBaseUrlIsNotHttps_FailsWithoutSendingTheToken()
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler("[]");
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetPipelineChecksAsync(
            new Uri("http://gitlab.example/api/v4"),
            ValidSlug,
            GitLabJobsByPipelineExample.PipelineId,
            "failed",
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Failure failure = result.ShouldBeOfType<Result<ProviderChecks>.Failure>();
        (failure.Error, handler.AllRequests.Count).ShouldBe((GitLabErrors.InvalidBaseUrl, 0));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task WhenJobsReadIsRefusedAsAuthFailure_ReturnsUnreadable(HttpStatusCode jobsStatus)
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler(string.Empty, jobsStatus);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Unreadable>();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task WhenJobsReadFailsOnTheServer_FailsTheChecksRead(HttpStatusCode jobsStatus)
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler(string.Empty, jobsStatus);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Failure failure = result.ShouldBeOfType<Result<ProviderChecks>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.UnexpectedStatusCode");
    }

    [Fact]
    public async Task WhenJobsReadIsTooManyRequests_FailsWithRateLimitExhausted()
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler(string.Empty, HttpStatusCode.TooManyRequests);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Failure failure = result.ShouldBeOfType<Result<ProviderChecks>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenJobsReadIsForbiddenByRateLimit_FailsWithRateLimitExhausted()
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler(string.Empty, HttpStatusCode.Forbidden);
        handler.ResponseHeaders["RateLimit-Remaining"] = "0";
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Failure failure = result.ShouldBeOfType<Result<ProviderChecks>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.RateLimitExhausted");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task WhenJobsReadIsRejectedAsAClientError_ReturnsRedNamingNothingAndLogsTheStatus(
        HttpStatusCode jobsStatus)
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler(string.Empty, jobsStatus);
        CapturingLogger<GitLabHttpClient> logger = new();
        GitLabHttpClient sut = BuildSut(handler, logger);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBeEmpty();
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains(((int)jobsStatus).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task WhenJobsReadFailsInTransport_FailsTheChecksRead(Func<Exception> transportFailure)
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler(string.Empty)
            .WithThrowingRoute($"/pipelines/{GitLabJobsByPipelineExample.PipelineId}/jobs", transportFailure);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Failure failure = result.ShouldBeOfType<Result<ProviderChecks>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.ProviderError");
    }

    [Fact]
    public async Task WhenCallerCancelsDuringJobsRead_PropagatesTheCancellation()
    {
        // Arrange
        using CancellationTokenSource cancellation = new();
        FakeHandler handler = FailedPipelineHandler(string.Empty)
            .WithThrowingRoute(
                $"/pipelines/{GitLabJobsByPipelineExample.PipelineId}/jobs",
                () =>
                {
                    cancellation.Cancel();
                    return new TaskCanceledException();
                });
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Task<Result<ProviderChecks>> read = sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            cancellation.Token);

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(async () => await read);
    }

    public static TheoryData<Func<Exception>> TransportFailures =>
        new()
        {
            () => new HttpRequestException("connection reset"),
            () => new TaskCanceledException(),
        };

    [Theory]
    [InlineData("success")]
    [InlineData("running")]
    [InlineData("canceled")]
    [InlineData("manual")]
    public async Task WhenHeadPipelineIsNotFailed_ReadsNoJobs(string pipelineStatus)
    {
        // Arrange
        string json = $$"""{ "iid": 42, "state": "opened", "head_pipeline": { "id": 6, "status": "{{pipelineStatus}}" } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        handler.AllRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenHeadPipelineFailedWithoutId_ReturnsRedNamingNothingAndReadsNoJobs()
    {
        // Arrange
        string json = """{ "iid": 42, "state": "opened", "head_pipeline": { "status": "failed" } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBeEmpty();
        handler.AllRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenHeadPipelineFailedWithoutId_LogsAWarning()
    {
        // Arrange
        string json = """{ "iid": 42, "state": "opened", "head_pipeline": { "status": "failed" } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        CapturingLogger<GitLabHttpClient> logger = new();
        GitLabHttpClient sut = BuildSut(handler, logger);

        // Act
        await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("carries no id"));
    }

    [Fact]
    public async Task WhenHeadPipelineStatusIsRunning_ReturnsPending()
    {
        // Arrange
        string json = """{ "iid": 42, "state": "opened", "head_pipeline": { "status": "running" } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Pending>();
    }

    [Fact]
    public async Task WhenHeadPipelineStatusIsCanceled_ReturnsUnfinished()
    {
        // Arrange
        string json = """{ "iid": 42, "state": "opened", "head_pipeline": { "status": "canceled" } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Unfinished>();
    }

    [Fact]
    public async Task WhenHeadPipelineStatusIsManual_ReturnsUnfinished()
    {
        // Arrange
        string json = """{ "iid": 42, "state": "opened", "head_pipeline": { "status": "manual" } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Unfinished>();
    }

    [Fact]
    public async Task WhenHeadPipelineIsNull_ReturnsNoneDetected()
    {
        // Arrange
        string json = """{ "iid": 42, "state": "opened", "head_pipeline": null }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.NoneDetected>();
    }

    [Fact]
    public async Task WhenHeadPipelineFieldIsAbsent_ReturnsNoneDetected()
    {
        // Arrange — MR response has no head_pipeline key at all
        string json = """{ "iid": 42, "state": "opened" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.NoneDetected>();
    }

    [Fact]
    public async Task WhenGitLabReturns401_ReturnsUnreadable()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Unauthorized, string.Empty);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert — 401 maps to Unreadable, not a Result failure
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Unreadable>();
    }

    [Fact]
    public async Task WhenGitLabReturns403WithoutRateLimitHeader_ReturnsUnreadable()
    {
        // Arrange — 403 without RateLimit-Remaining: 0 is an auth failure, not rate-limit
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Unreadable>();
    }

    [Fact]
    public async Task WhenGitLabReturns403WithRateLimitExhausted_ReturnsRateLimitFailure()
    {
        // Arrange — 403 with RateLimit-Remaining: 0 is a rate-limit failure, not Unreadable
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["RateLimit-Remaining"] = "0";
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<ProviderChecks>.Failure failure = result.ShouldBeOfType<Result<ProviderChecks>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectMergeRequestEndpoint()
    {
        // Arrange
        string json = """{ "iid": 42, "state": "opened", "head_pipeline": { "status": "success" } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/merge_requests/42");
    }
}
