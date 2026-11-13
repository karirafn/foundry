using System.Net;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Merge;
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

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.GitHubMergeTests;

/// <summary>
/// The GitHub side of Foundry's own merge (ADR 0105): reading the repository's allowed merge methods, then
/// <c>PUT repos/{owner}/{repo}/pulls/{number}/merge</c> pinned to the checked head. Status codes come from
/// GitHub's REST docs, https://docs.github.com/en/rest/pulls/pulls#merge-a-pull-request (200 merged, 403
/// forbidden, 404 not found, 405 merge cannot be performed, 409 sha did not match the head, 422 validation).
/// </summary>
public sealed class GitHubMerge
{
    private static readonly Uri BaseUrl = new("https://api.github.com");
    private const string PrUrl = "https://github.com/owner/repo/pull/42";
    private const string HeadSha = "1111111111111111111111111111111111111111";
    private const string Token = "ghp_token";
    private const string RateLimitRemainingHeader = "X-RateLimit-Remaining";

    private static RepositorySlug Slug => RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(RecordingHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    private static string RepoJson(bool squash = true, bool rebase = true, bool merge = true) =>
        $$"""
        {
          "full_name": "owner/repo",
          "default_branch": "main",
          "allow_squash_merge": {{(squash ? "true" : "false")}},
          "allow_rebase_merge": {{(rebase ? "true" : "false")}},
          "allow_merge_commit": {{(merge ? "true" : "false")}}
        }
        """;

    private const string MergedJson =
        """{ "sha": "2222222222222222222222222222222222222222", "merged": true, "message": "Pull Request successfully merged" }""";

    private static Task<Result<MergeRequestOutcome>> MergeAsync(RecordingHandler handler, string expectedHeadSha = HeadSha) =>
        BuildSut(handler).MergePullRequestAsync(BaseUrl, Slug, PrUrl, expectedHeadSha, Token, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(true, true, true, "squash")]
    [InlineData(true, false, false, "squash")]
    [InlineData(false, true, true, "rebase")]
    [InlineData(false, true, false, "rebase")]
    [InlineData(false, false, true, "merge")]
    public async Task Merge_UsesFirstAllowedMethodInOrderSquashRebaseMerge(bool squash, bool rebase, bool merge, string expected)
    {
        // Arrange
        RecordingHandler handler = new(
            RecordingHandler.Respond(HttpStatusCode.OK, RepoJson(squash, rebase, merge)),
            RecordingHandler.Respond(HttpStatusCode.OK, MergedJson));

        // Act
        await MergeAsync(handler);

        // Assert
        using JsonDocument body = JsonDocument.Parse(handler.Requests[1].Body!);
        body.RootElement.GetProperty("merge_method").GetString().ShouldBe(expected);
    }

    [Fact]
    public async Task Merge_WhenNoMethodIsAllowed_IsNotMergeableWithoutMergeCall()
    {
        // Arrange
        RecordingHandler handler = new(
            RecordingHandler.Respond(HttpStatusCode.OK, RepoJson(squash: false, rebase: false, merge: false)));

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ValueOrThrow().ShouldBe(new MergeRequestOutcome.NotMergeable("no_merge_method_allowed"));
        handler.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Get);
    }

    [Fact]
    public async Task Merge_ReadsRepositorySettingsFromRepoEndpoint()
    {
        // Arrange
        RecordingHandler handler = new(
            RecordingHandler.Respond(HttpStatusCode.OK, RepoJson()),
            RecordingHandler.Respond(HttpStatusCode.OK, MergedJson));

        // Act
        await MergeAsync(handler);

        // Assert
        RecordedRequest read = handler.Requests[0];
        read.Method.ShouldBe(HttpMethod.Get);
        read.Uri.ShouldBe("https://api.github.com/repos/owner/repo");
    }

    [Fact]
    public async Task Merge_PutsToPullMergeEndpointPinnedToExpectedHead()
    {
        // Arrange
        RecordingHandler handler = new(
            RecordingHandler.Respond(HttpStatusCode.OK, RepoJson()),
            RecordingHandler.Respond(HttpStatusCode.OK, MergedJson));

        // Act
        await MergeAsync(handler);

        // Assert
        RecordedRequest put = handler.Requests[1];
        put.Method.ShouldBe(HttpMethod.Put);
        put.Uri.ShouldBe("https://api.github.com/repos/owner/repo/pulls/42/merge");
        using JsonDocument body = JsonDocument.Parse(put.Body!);
        body.RootElement.GetProperty("sha").GetString().ShouldBe(HeadSha);
    }

    [Fact]
    public async Task Merge_When200_IsMerged()
    {
        // Arrange
        RecordingHandler handler = new(
            RecordingHandler.Respond(HttpStatusCode.OK, RepoJson()),
            RecordingHandler.Respond(HttpStatusCode.OK, MergedJson));

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ValueOrThrow().ShouldBeOfType<MergeRequestOutcome.Merged>();
    }

    [Fact]
    public async Task Merge_When409_HeadMoved()
    {
        // Arrange
        RecordingHandler handler = new(
            RecordingHandler.Respond(HttpStatusCode.OK, RepoJson()),
            RecordingHandler.Respond(HttpStatusCode.Conflict, """{ "message": "Head branch was modified. Review and try the merge again." }"""));

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ValueOrThrow().ShouldBeOfType<MergeRequestOutcome.HeadMoved>();
    }

    [Fact]
    public async Task Merge_When405_IsNotMergeableWithResponseMessageAsReason()
    {
        // Arrange
        RecordingHandler handler = new(
            RecordingHandler.Respond(HttpStatusCode.OK, RepoJson()),
            RecordingHandler.Respond(HttpStatusCode.MethodNotAllowed, """{ "message": "Pull Request is not mergeable" }"""));

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ValueOrThrow().ShouldBe(new MergeRequestOutcome.NotMergeable("Pull Request is not mergeable"));
    }

    [Fact]
    public async Task Merge_When405WithoutMessage_IsNotMergeableUnrecognised()
    {
        // Arrange
        RecordingHandler handler = new(
            RecordingHandler.Respond(HttpStatusCode.OK, RepoJson()),
            RecordingHandler.Respond(HttpStatusCode.MethodNotAllowed, "not json"));

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ValueOrThrow().ShouldBe(new MergeRequestOutcome.NotMergeable("unrecognised_error"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Merge_WhenMergeCallIsUnauthorized_IsUnauthorized(HttpStatusCode status)
    {
        // Arrange
        RecordingHandler handler = new(
            RecordingHandler.Respond(HttpStatusCode.OK, RepoJson()),
            RecordingHandler.Respond(status, """{ "message": "Resource not accessible by personal access token" }"""));

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ValueOrThrow().ShouldBeOfType<MergeRequestOutcome.Unauthorized>();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Merge_WhenSettingsReadIsUnauthorized_IsUnauthorizedWithoutMergeCall(HttpStatusCode status)
    {
        // Arrange
        RecordingHandler handler = new(RecordingHandler.Respond(status, """{ "message": "Bad credentials" }"""));

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ValueOrThrow().ShouldBeOfType<MergeRequestOutcome.Unauthorized>();
        handler.Requests.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Merge_WhenMergeCallIsTransient_Fails(HttpStatusCode status)
    {
        // Arrange
        RecordingHandler handler = new(
            RecordingHandler.Respond(HttpStatusCode.OK, RepoJson()),
            RecordingHandler.Respond(status, "{}"));

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<MergeRequestOutcome>.Failure>();
    }

    [Fact]
    public async Task Merge_WhenMergeCallIsRateLimited403_Fails()
    {
        // Arrange
        RecordingHandler handler = new(
            RecordingHandler.Respond(HttpStatusCode.OK, RepoJson()),
            RecordingHandler.Respond(HttpStatusCode.Forbidden, """{ "message": "API rate limit exceeded" }""", rateLimitRemaining: "0"));

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<MergeRequestOutcome>.Failure>()
            .Error.ShouldBe(GitHubErrors.RateLimitExhausted);
    }

    [Fact]
    public async Task Merge_WhenSettingsReadIsTransient_FailsWithoutMergeCall()
    {
        // Arrange
        RecordingHandler handler = new(RecordingHandler.Respond(HttpStatusCode.BadGateway, "{}"));

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<MergeRequestOutcome>.Failure>();
        handler.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Merge_WhenRequestTimesOut_Fails()
    {
        // Arrange
        RecordingHandler handler = new(
            RecordingHandler.Respond(HttpStatusCode.OK, RepoJson()),
            RecordingHandler.TimesOut);

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<MergeRequestOutcome>.Failure>();
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Merge_WhenStatusIsUnrecognised_IsNotMergeableUnrecognised(HttpStatusCode status)
    {
        // Arrange
        RecordingHandler handler = new(
            RecordingHandler.Respond(HttpStatusCode.OK, RepoJson()),
            RecordingHandler.Respond(status, """{ "message": "Something new happened." }"""));

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ValueOrThrow().ShouldBe(new MergeRequestOutcome.NotMergeable("unrecognised_error"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Merge_WhenExpectedHeadIsBlank_ThrowsBeforeAnyCall(string? expectedHeadSha)
    {
        // Arrange
        RecordingHandler handler = new();

        // Act & Assert
        await Should.ThrowAsync<ArgumentException>(() => MergeAsync(handler, expectedHeadSha!));
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Merge_WhenBaseUrlIsNotHttps_FailsWithInvalidBaseUrl()
    {
        // Arrange
        RecordingHandler handler = new();

        // Act
        Result<MergeRequestOutcome> result = await BuildSut(handler).MergePullRequestAsync(
            new Uri("http://api.github.com"), Slug, PrUrl, HeadSha, Token, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<MergeRequestOutcome>.Failure>().Error.ShouldBe(GitHubErrors.InvalidBaseUrl);
        handler.Requests.ShouldBeEmpty();
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string? Body);

    private sealed class RecordingHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _index;

        public List<RecordedRequest> Requests { get; } = [];

        public static Func<HttpResponseMessage> Respond(HttpStatusCode status, string body, string? rateLimitRemaining = null) =>
            () =>
            {
                HttpResponseMessage response = new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                if (rateLimitRemaining is not null)
                {
                    response.Headers.Add(RateLimitRemainingHeader, rateLimitRemaining);
                }

                return response;
            };

        public static HttpResponseMessage TimesOut() =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.ToString(), body));
            return responses[_index++]();
        }
    }
}
