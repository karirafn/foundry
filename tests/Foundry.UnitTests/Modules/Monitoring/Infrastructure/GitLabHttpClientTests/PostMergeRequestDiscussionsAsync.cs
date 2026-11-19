using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
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

public sealed class PostMergeRequestDiscussionsAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat-token";
    private const string MergeRequestUrl = "https://gitlab.com/group/project/-/merge_requests/11";
    private const string Head = "33e2ee8579fda5bc36accc9c6fbd0b4fefda9e30";

    // Shaped on the example responses of GitLab's REST reference "Merge requests: get merge request diff versions" and
    // "Discussions: create a new thread in the merge request diff". No captured response from a live call was available.
    private const string VersionsJson = """
        [
          {
            "id": 110,
            "head_commit_sha": "33e2ee8579fda5bc36accc9c6fbd0b4fefda9e30",
            "base_commit_sha": "eeb57dffe83deb686a60a71c16c32f71046868fd",
            "start_commit_sha": "eeb57dffe83deb686a60a71c16c32f71046868fd",
            "created_at": "2016-07-26T14:44:48.926Z",
            "merge_request_id": 105,
            "state": "collected",
            "real_size": "1",
            "patch_id_sha": "d504412d5b6e6739647e752aff8e468dde093f2f"
          },
          {
            "id": 108,
            "head_commit_sha": "3eed087b29835c48015768f839d76e5ea8f07a24",
            "base_commit_sha": "eeb57dffe83deb686a60a71c16c32f71046868fd",
            "start_commit_sha": "eeb57dffe83deb686a60a71c16c32f71046868fd",
            "created_at": "2016-07-25T14:21:33.028Z",
            "merge_request_id": 105,
            "state": "collected",
            "real_size": "1",
            "patch_id_sha": "72c30d1f0115fc1d2bb0b29b24dc2982cbcdfd32"
          }
        ]
        """;

    private static string DiscussionJson(long noteId) => $$"""
        {
          "id": "6a9c1750b37d513a43987b574953fceb50b03ce7",
          "individual_note": false,
          "notes": [
            {
              "id": {{noteId}},
              "type": "DiffNote",
              "body": "Retries forever. (correctness)",
              "author": { "id": 1, "username": "root", "name": "Administrator", "state": "active" },
              "created_at": "2018-03-03T21:54:39.668Z",
              "updated_at": "2018-03-03T21:54:39.668Z",
              "system": false,
              "noteable_id": 3,
              "noteable_type": "MergeRequest",
              "noteable_iid": 11,
              "resolvable": true,
              "resolved": false,
              "position": {
                "base_sha": "eeb57dffe83deb686a60a71c16c32f71046868fd",
                "start_sha": "eeb57dffe83deb686a60a71c16c32f71046868fd",
                "head_sha": "33e2ee8579fda5bc36accc9c6fbd0b4fefda9e30",
                "old_path": "src/Retry.cs",
                "new_path": "src/Retry.cs",
                "position_type": "text",
                "old_line": null,
                "new_line": 12
              }
            }
          ]
        }
        """;

    private static readonly InlineComment[] Comments =
    [
        new("src/Retry.cs", 12, "Retries forever. (correctness)"),
        new("src/Cache.cs", 3, "Unbounded. (performance)"),
    ];

    private static RepositorySlug ValidSlug => RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient BuildSut(DelegatingHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    private static Task<Result<InlineReviewPosting>> PostAsync(GitLabHttpClient sut, string commit = Head) =>
        sut.PostMergeRequestDiscussionsAsync(
            ValidBaseUrl, ValidSlug, MergeRequestUrl, commit, Comments, ValidToken, CancellationToken.None);

    [Fact]
    public async Task WhenPosted_CreatesOneDiffDiscussionPerCommentOnTheLatestVersion()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, VersionsJson),
            (HttpStatusCode.Created, DiscussionJson(1126)),
            (HttpStatusCode.Created, DiscussionJson(1127)),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        await PostAsync(sut);

        // Assert
        handler.ShouldSatisfyAllConditions(
            () => handler.Requests.Select(r => r.Method).ShouldBe([HttpMethod.Get, HttpMethod.Post, HttpMethod.Post]),
            () => handler.Requests[0].RequestUri!.AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/merge_requests/11/versions"),
            () => handler.Requests[1].RequestUri!.AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/merge_requests/11/discussions"),
            () => handler.RequestBodies[1].ShouldBe(
                """{"body":"Retries forever. (correctness)","position":{"position_type":"text","base_sha":"eeb57dffe83deb686a60a71c16c32f71046868fd","start_sha":"eeb57dffe83deb686a60a71c16c32f71046868fd","head_sha":"33e2ee8579fda5bc36accc9c6fbd0b4fefda9e30","new_path":"src/Retry.cs","new_line":12}}"""));
    }

    [Fact]
    public async Task WhenGitLabCreatesTheDiscussions_RegistersEachNotesOwnId()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, VersionsJson),
            (HttpStatusCode.Created, DiscussionJson(1126)),
            (HttpStatusCode.Created, DiscussionJson(1127)),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        InlineReviewPosting posting = (await PostAsync(sut)).ValueOrThrow();

        // Assert
        posting.Posted.ShouldBe(
        [
            new PostedInlineComment(Comments[0], PostedCommentKey.ReviewNote(ProviderCommentId.Create(1126).ValueOrThrow())),
            new PostedInlineComment(Comments[1], PostedCommentKey.ReviewNote(ProviderCommentId.Create(1127).ValueOrThrow())),
        ]);
    }

    [Fact]
    public async Task WhenGitLabRejectsOneDiscussion_PostsTheOthersAndReportsTheRejectedOne()
    {
        // Arrange — the shape GitLab's docs give for a bad request
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, VersionsJson),
            (HttpStatusCode.BadRequest, """{ "message": "400 Bad request - Note {:line_code=>[\"can't be blank\"]}" }"""),
            (HttpStatusCode.Created, DiscussionJson(1127)),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        InlineReviewPosting posting = (await PostAsync(sut)).ValueOrThrow();

        // Assert
        posting.ShouldSatisfyAllConditions(
            () => posting.Posted.ShouldHaveSingleItem().Comment.ShouldBe(Comments[1]),
            () => posting.Failed.ShouldHaveSingleItem().Comment.ShouldBe(Comments[0]),
            () => posting.Failed[0].Error.Code.ShouldBe(CommentPostErrors.PermanentCode));
    }

    [Fact]
    public async Task WhenTheLatestVersionIsNotTheReviewedCommit_PostsNothing()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, VersionsJson)]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<InlineReviewPosting> result = await PostAsync(sut, commit: "3eed087b29835c48015768f839d76e5ea8f07a24");

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(CommentPostErrors.PermanentCode),
            () => handler.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Get));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, CommentPostErrors.TransientCode)]
    [InlineData(HttpStatusCode.NotFound, CommentPostErrors.PermanentCode)]
    public async Task WhenTheVersionsReadFails_PostsNothing(HttpStatusCode status, string expectedCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(status, """{ "message": "error" }""")]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<InlineReviewPosting> result = await PostAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(expectedCode),
            () => handler.Requests.Count.ShouldBe(1));
    }
}
