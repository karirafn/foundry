using System.Net;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Merge;
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

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.GitLabMergeTests;

/// <summary>
/// The GitLab side of Foundry's own merge (ADR 0105): reading the project's <c>squash_option</c>, then
/// <c>PUT projects/:id/merge_requests/:iid/merge</c> pinned to the checked head. Status codes and messages come
/// from GitLab's "Merge a merge request" docs, https://docs.gitlab.com/api/merge_requests/#merge-a-merge-request
/// (read 2026-11-13): 401 "401 Unauthorized", 405 "405 Method Not Allowed", 409 "SHA does not match HEAD of
/// source branch", 422 "Branch cannot be merged".
/// </summary>
public sealed class GitLabMerge
{
    private static readonly Uri BaseUrl = new("https://gitlab.com/api/v4");
    private const string MrUrl = "https://gitlab.com/group/project/-/merge_requests/7";
    private const string HeadSha = "1111111111111111111111111111111111111111";
    private const string Token = "glpat-token";
    private const string RateLimitRemainingHeader = "RateLimit-Remaining";

    private static RepositorySlug Slug => RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient BuildSut(RecordingHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    // squash_option values per GitLab's Projects API docs: never, always, default_on, default_off.
    private static string ProjectJson(string squashOption = "default_off") =>
        $$"""{ "id": 99, "path_with_namespace": "group/project", "merge_method": "ff", "squash_option": "{{squashOption}}" }""";

    private const string MergedJson = """{ "iid": 7, "state": "merged", "sha": "1111111111111111111111111111111111111111" }""";

    private static Task<Result<MergeRequestOutcome>> MergeAsync(RecordingHandler handler, string expectedHeadSha = HeadSha) =>
        BuildSut(handler).MergePullRequestAsync(BaseUrl, Slug, MrUrl, expectedHeadSha, Token, TestContext.Current.CancellationToken);

    private static RecordingHandler ProjectThenMerge(HttpStatusCode status, string body) =>
        new(RecordingHandler.Respond(HttpStatusCode.OK, ProjectJson()), RecordingHandler.Respond(status, body));

    [Theory]
    [InlineData("always", true)]
    [InlineData("default_on", true)]
    [InlineData("default_off", true)]
    [InlineData("never", false)]
    public async Task Merge_SquashesUnlessSquashOptionIsNever(string squashOption, bool expected)
    {
        // Arrange
        RecordingHandler handler = new(
            RecordingHandler.Respond(HttpStatusCode.OK, ProjectJson(squashOption)),
            RecordingHandler.Respond(HttpStatusCode.OK, MergedJson));

        // Act
        await MergeAsync(handler);

        // Assert
        using JsonDocument body = JsonDocument.Parse(handler.Requests[1].Body!);
        body.RootElement.GetProperty("squash").GetBoolean().ShouldBe(expected);
    }

    [Fact]
    public async Task Merge_ReadsSquashOptionFromProjectEndpoint()
    {
        // Arrange
        RecordingHandler handler = ProjectThenMerge(HttpStatusCode.OK, MergedJson);

        // Act
        await MergeAsync(handler);

        // Assert
        RecordedRequest read = handler.Requests[0];
        read.Method.ShouldBe(HttpMethod.Get);
        read.Uri.ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject");
    }

    [Fact]
    public async Task Merge_PutsToMergeEndpointPinnedToExpectedHeadWithoutOverridingMergeMethod()
    {
        // Arrange
        RecordingHandler handler = ProjectThenMerge(HttpStatusCode.OK, MergedJson);

        // Act
        await MergeAsync(handler);

        // Assert
        RecordedRequest put = handler.Requests[1];
        put.Method.ShouldBe(HttpMethod.Put);
        put.Uri.ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/merge_requests/7/merge");
        using JsonDocument body = JsonDocument.Parse(put.Body!);
        body.RootElement.ShouldSatisfyAllConditions(
            () => body.RootElement.GetProperty("sha").GetString().ShouldBe(HeadSha),
            () => body.RootElement.TryGetProperty("merge_method", out _).ShouldBeFalse(),
            () => body.RootElement.TryGetProperty("merge_when_pipeline_succeeds", out _).ShouldBeFalse(),
            () => body.RootElement.TryGetProperty("auto_merge", out _).ShouldBeFalse());
    }

    [Fact]
    public async Task Merge_When200_IsMerged()
    {
        // Arrange
        RecordingHandler handler = ProjectThenMerge(HttpStatusCode.OK, MergedJson);

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ValueOrThrow().ShouldBeOfType<MergeRequestOutcome.Merged>();
    }

    [Fact]
    public async Task Merge_When409ShaMismatch_HeadMoved()
    {
        // Arrange
        RecordingHandler handler = ProjectThenMerge(
            HttpStatusCode.Conflict, """{ "message": "SHA does not match HEAD of source branch" }""");

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ValueOrThrow().ShouldBeOfType<MergeRequestOutcome.HeadMoved>();
    }

    [Theory]
    [InlineData(HttpStatusCode.MethodNotAllowed, "405 Method Not Allowed")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "Branch cannot be merged")]
    public async Task Merge_WhenCannotMerge_IsNotMergeableWithResponseMessageAsReason(HttpStatusCode status, string message)
    {
        // Arrange
        RecordingHandler handler = ProjectThenMerge(status, $$"""{ "message": "{{message}}" }""");

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ValueOrThrow().ShouldBe(new MergeRequestOutcome.NotMergeable(message));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "401 Unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "403 Forbidden")]
    public async Task Merge_WhenMergeCallIsUnauthorized_IsUnauthorized(HttpStatusCode status, string message)
    {
        // Arrange
        RecordingHandler handler = ProjectThenMerge(status, $$"""{ "message": "{{message}}" }""");

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ValueOrThrow().ShouldBeOfType<MergeRequestOutcome.Unauthorized>();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Merge_WhenProjectReadIsUnauthorized_IsUnauthorizedWithoutMergeCall(HttpStatusCode status)
    {
        // Arrange
        RecordingHandler handler = new(RecordingHandler.Respond(status, """{ "message": "401 Unauthorized" }"""));

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
        RecordingHandler handler = ProjectThenMerge(status, "{}");

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
            RecordingHandler.Respond(HttpStatusCode.OK, ProjectJson()),
            RecordingHandler.Respond(HttpStatusCode.Forbidden, """{ "message": "403 Forbidden" }""", rateLimitRemaining: "0"));

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<MergeRequestOutcome>.Failure>()
            .Error.ShouldBe(GitLabErrors.RateLimitExhausted);
    }

    [Fact]
    public async Task Merge_WhenProjectReadIsTransient_FailsWithoutMergeCall()
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
            RecordingHandler.Respond(HttpStatusCode.OK, ProjectJson()),
            RecordingHandler.TimesOut);

        // Act
        Result<MergeRequestOutcome> result = await MergeAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<MergeRequestOutcome>.Failure>();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{ "message": "SHA must be provided when merging" }""")]
    [InlineData(HttpStatusCode.NotFound, """{ "message": "404 Not found" }""")]
    [InlineData(HttpStatusCode.MethodNotAllowed, """{ "message": { "base": ["Something new"] } }""")]
    public async Task Merge_WhenResponseIsUnrecognised_IsNotMergeableUnrecognised(HttpStatusCode status, string body)
    {
        // Arrange
        RecordingHandler handler = ProjectThenMerge(status, body);

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
            new Uri("http://gitlab.com/api/v4"), Slug, MrUrl, HeadSha, Token, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<MergeRequestOutcome>.Failure>().Error.ShouldBe(GitLabErrors.InvalidBaseUrl);
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
