using System.Net;
using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
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

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabIssueProviderTests;

public sealed class MarkPullRequestReadyAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat_token";
    private const string MergeRequestUrl = "https://gitlab.com/group/project/-/merge_requests/12";
    private const string MergeRequestPath = "/api/v4/projects/group%2Fproject/merge_requests/12";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabIssueProvider BuildSut(HttpMessageHandler handler)
    {
        GitLabHttpClient gitLabHttpClient = new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);
        return new GitLabIssueProvider(
            gitLabHttpClient, new ActionableFeedbackPolicy(TimeProvider.System), ValidToken, ValidBaseUrl);
    }

    private static string MergeRequestJson(string title) =>
        JsonSerializer.Serialize(new { iid = 12, title, state = "opened" });

    private static Task<Result> MarkReadyAsync(GitLabIssueProvider sut, string mergeRequestUrl = MergeRequestUrl) =>
        sut.MarkPullRequestReadyAsync(ValidSlug, mergeRequestUrl, CancellationToken.None);

    private static string? SentTitle(SequentialFakeHandler handler)
    {
        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[1]!);
        return body.RootElement.GetProperty("title").GetString();
    }

    [Fact]
    public async Task WhenTitleIsDraft_ReadsTheMergeRequestThenPutsTheStrippedTitle()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, MergeRequestJson("Draft: feat: add retry")),
            (HttpStatusCode.OK, MergeRequestJson("feat: add retry")),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsSuccess.ShouldBeTrue(),
            () => handler.Requests[0].Method.ShouldBe(HttpMethod.Get),
            () => handler.Requests[0].RequestUri!.AbsolutePath.ShouldBe(MergeRequestPath),
            () => handler.Requests[1].Method.ShouldBe(HttpMethod.Put),
            () => handler.Requests[1].RequestUri!.AbsolutePath.ShouldBe(MergeRequestPath),
            () => handler.Requests[1].Headers.GetValues("PRIVATE-TOKEN").ShouldBe([ValidToken]),
            () => handler.RequestBodies[1].ShouldBe("""{"title":"feat: add retry"}"""));
    }

    [Theory]
    [InlineData("Draft: feat: add retry", "feat: add retry")]
    [InlineData("[Draft] feat: add retry", "feat: add retry")]
    [InlineData("(Draft) feat: add retry", "feat: add retry")]
    [InlineData("draft: feat: add retry", "feat: add retry")]
    [InlineData("[DRAFT] feat: add retry", "feat: add retry")]
    [InlineData("Draft:feat: add retry", "feat: add retry")]
    [InlineData("Draft:   feat: add  retry ", "feat: add  retry ")]
    [InlineData("Draft: Draft: feat: add retry", "Draft: feat: add retry")]
    [InlineData("Draft: feat: draft the release notes", "feat: draft the release notes")]
    public async Task WhenTitleStartsWithADraftPrefix_SendsTheTitleWithOnlyThatPrefixRemoved(string title, string expected)
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, MergeRequestJson(title)),
            (HttpStatusCode.OK, MergeRequestJson(expected)),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsSuccess.ShouldBeTrue(),
            () => SentTitle(handler).ShouldBe(expected));
    }

    [Theory]
    [InlineData("feat: add retry")]
    [InlineData("feat: draft the release notes")]
    [InlineData("feat: keep Draft: in the middle")]
    [InlineData("Drafting: a title that only starts like a prefix")]
    public async Task WhenTitleHasNoDraftPrefix_SucceedsWithoutAPut(string title)
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, MergeRequestJson(title))]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsSuccess.ShouldBeTrue(),
            () => handler.Requests.Count.ShouldBe(1));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task WhenTheReadAnswersRetryableStatus_FailsTransientWithoutAPut(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(statusCode, "{}")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => RunPublishingErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue(),
            () => handler.Requests.Count.ShouldBe(1));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task WhenTheReadIsRejected_FailsPermanentWithoutAPut(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(statusCode, """{ "message": "nope" }""")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode),
            () => handler.Requests.Count.ShouldBe(1));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task WhenThePutAnswersRetryableStatus_FailsTransient(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, MergeRequestJson("Draft: feat: add retry")), (statusCode, "{}")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        RunPublishingErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task WhenThePutIsRejected_FailsPermanent(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new(
            [(HttpStatusCode.OK, MergeRequestJson("Draft: feat: add retry")), (statusCode, """{ "message": "nope" }""")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode);
    }

    [Fact]
    public async Task WhenTheRequestTimesOut_FailsTransient()
    {
        // Arrange
        ThrowingHandler handler = new(new TaskCanceledException("timeout"));
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        RunPublishingErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTheMergeRequestUrlHasNoIid_FailsPermanentWithoutCallingProvider()
    {
        // Arrange
        SequentialFakeHandler handler = new(Array.Empty<(HttpStatusCode, string)>());
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut, "https://gitlab.com/group/project/-/issues");

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode),
            () => handler.Requests.ShouldBeEmpty());
    }
}
