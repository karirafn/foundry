using System.Net;

using Foundry.Modules.Monitoring.Contracts;
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

using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetPullRequestStateAsync
{
    private const string MergeRequestUrl = "https://gitlab.com/group/project/-/merge_requests/1";

    private const string Head = "8888888888888888888888888888888888888888";

    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient BuildSut(FakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    private static Task<Result<PullRequestState>> ReadAsync(string body) =>
        BuildSut(new FakeHandler(HttpStatusCode.OK, body)).GetPullRequestStateAsync(
            ValidBaseUrl,
            ValidSlug,
            MergeRequestUrl,
            token: "glpat_token",
            CancellationToken.None);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenMergeRequestIsOpenedWithASha_ReturnsOpenAtThatHeadWithItsDraftFlag(bool isDraft)
    {
        // Arrange — field shape per GitLab's "Get single MR" response ("sha": the source branch head; boolean "draft").
        string body = $$"""{ "iid": 1, "state": "opened", "sha": "{{Head}}", "draft": {{(isDraft ? "true" : "false")}} }""";

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        PullRequestState.Open open = result.ShouldBeOfType<Result<PullRequestState>.Success>().Value
            .ShouldBeOfType<PullRequestState.Open>();
        open.ShouldSatisfyAllConditions(
            () => open.Head.Value.ShouldBe(Head),
            () => open.IsDraft.ShouldBe(isDraft));
    }

    [Fact]
    public async Task WhenMergeRequestIsLocked_ReturnsOpen()
    {
        // Arrange — GitLab reports "locked" while a merge request is being merged.
        string body = $$"""{ "iid": 1, "state": "locked", "sha": "{{Head}}" }""";

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Success>().Value
            .ShouldBeOfType<PullRequestState.Open>().Head.Value.ShouldBe(Head);
    }

    [Fact]
    public async Task WhenMergeRequestIsMerged_ReturnsMerged()
    {
        // Arrange
        string body = $$"""{ "iid": 1, "state": "merged", "sha": "{{Head}}" }""";

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Success>().Value.ShouldBeOfType<PullRequestState.Merged>();
    }

    [Fact]
    public async Task WhenMergeRequestIsClosed_ReturnsClosed()
    {
        // Arrange
        string body = $$"""{ "iid": 1, "state": "closed", "sha": "{{Head}}" }""";

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Success>().Value.ShouldBeOfType<PullRequestState.Closed>();
    }

    [Fact]
    public async Task WhenMergedMergeRequestHasNoSha_StillReturnsMerged()
    {
        // Arrange
        string body = """{ "iid": 1, "state": "merged" }""";

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Success>().Value.ShouldBeOfType<PullRequestState.Merged>();
    }

    [Fact]
    public async Task WhenStateIsUnrecognised_FailsNamingTheState()
    {
        // Arrange
        string body = $$"""{ "iid": 1, "state": "archived", "sha": "{{Head}}" }""";

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        Error error = result.ShouldBeOfType<Result<PullRequestState>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(PullRequestStateErrors.UnrecognisedCode),
            () => error.Message.ShouldContain("'archived'"));
    }

    [Fact]
    public async Task WhenOpenedMergeRequestHasNoSha_FailsWithMissingHead()
    {
        // Arrange
        string body = """{ "iid": 1, "state": "opened" }""";

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Failure>().Error.Code
            .ShouldBe(PullRequestStateErrors.MissingHeadCode);
    }

    [Fact]
    public async Task WhenOpenedMergeRequestHasAMalformedSha_FailsWithMissingHead()
    {
        // Arrange — uppercase hex is not a CommitSha.
        string body = """{ "iid": 1, "state": "opened", "sha": "ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCD" }""";

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Failure>().Error.Code
            .ShouldBe(PullRequestStateErrors.MissingHeadCode);
    }

    [Fact]
    public async Task WhenGitLabReturnsNonSuccessStatus_PassesTheFailureThrough()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);

        // Act
        Result<PullRequestState> result = await BuildSut(handler).GetPullRequestStateAsync(
            ValidBaseUrl, ValidSlug, MergeRequestUrl, "glpat_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Failure>().Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenMergeRequestUrlCannotBeParsed_FailsWithInvalidMergeRequestUrl()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");

        // Act
        Result<PullRequestState> result = await BuildSut(handler).GetPullRequestStateAsync(
            ValidBaseUrl, ValidSlug, "https://gitlab.com/group/project/-/issues/1", "glpat_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Failure>().Error.Code.ShouldBe("GitLab.InvalidMergeRequestUrl");
    }

    [Fact]
    public async Task WhenBaseUrlIsNotHttps_FailsWithInvalidBaseUrl()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");

        // Act
        Result<PullRequestState> result = await BuildSut(handler).GetPullRequestStateAsync(
            new Uri("ftp://gitlab.com/api/v4"), ValidSlug, MergeRequestUrl, "glpat_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Failure>().Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenStateIsRead_ReadsTheSingleMergeRequestEndpoint()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, $$"""{ "iid": 1, "state": "opened", "sha": "{{Head}}" }""");

        // Act
        await BuildSut(handler).GetPullRequestStateAsync(ValidBaseUrl, ValidSlug, MergeRequestUrl, "glpat_token", CancellationToken.None);

        // Assert
        handler.LastRequest.ShouldNotBeNull().RequestUri.ShouldNotBeNull()
            .AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/merge_requests/1");
    }

    [Fact]
    public async Task WhenOpenedMergeRequestHasNoSha_ChecksAreStillRead()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, """{ "iid": 1, "state": "opened", "head_pipeline": { "id": 5, "status": "success" } }""");

        // Act
        Result<ProviderChecks> checks = await BuildSut(handler).GetChecksAsync(
            ValidBaseUrl, ValidSlug, MergeRequestUrl, "glpat_token", CancellationToken.None);

        // Assert
        checks.ShouldBeOfType<Result<ProviderChecks>.Success>().Value.ShouldBeOfType<ProviderChecks.Green>();
    }

    [Theory]
    [InlineData("opened", MergeRequestPresence.Open)]
    [InlineData("locked", MergeRequestPresence.Open)]
    [InlineData("merged", MergeRequestPresence.Merged)]
    [InlineData("closed", MergeRequestPresence.Closed)]
    [InlineData("OPENED", MergeRequestPresence.Open)]
    [InlineData("Merged", MergeRequestPresence.Merged)]
    [InlineData("open", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParsePullRequestState_MapsEachGitLabStateIgnoringCase(string? state, MergeRequestPresence? expected)
    {
        // Act
        MergeRequestPresence? presence = GitLabHttpClient.ParsePullRequestState(state);

        // Assert
        presence.ShouldBe(expected);
    }
}
