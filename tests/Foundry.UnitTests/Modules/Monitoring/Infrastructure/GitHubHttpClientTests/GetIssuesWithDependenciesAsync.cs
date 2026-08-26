using System.Net;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class GetIssuesWithDependenciesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

    // --- Issue field mapping ---

    [Fact]
    public async Task WhenGraphQlReturnsIssues_ParsesFieldsIdenticalToRestPath()
    {
        // Arrange
        string json = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "defaultBranchRef": { "name": "main" },
                  "issues": {
                    "pageInfo": { "hasNextPage": false, "endCursor": null },
                    "nodes": [
                      {
                        "number": 42,
                        "title": "Fix the bug",
                        "body": "Bug description",
                        "url": "https://github.com/owner/repo/issues/42",
                        "state": "OPEN",
                        "author": { "login": "octocat" },
                        "labels": { "nodes": [ { "name": "bug" }, { "name": "foundry" } ] },
                        "blockedBy": {
                          "totalCount": 0,
                          "pageInfo": { "hasNextPage": false },
                          "nodes": []
                        }
                      }
                    ]
                  }
                }
              },
              "errors": null
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token123",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        IReadOnlyList<ProviderIssue> issues = success.Value.Listing.Issues;
        issues.Count.ShouldBe(1);
        ProviderIssue issue = issues[0];
        issue.ShouldSatisfyAllConditions(
            () => issue.Number.ShouldBe(42),
            () => issue.Title.ShouldBe("Fix the bug"),
            () => issue.Body.ShouldBe("Bug description"),
            () => issue.Author.ShouldBe("octocat"),
            () => issue.Url.ShouldBe("https://github.com/owner/repo/issues/42"),
            () => issue.Labels.ShouldBe(["bug", "foundry"]),
            () => issue.IssueKindLabel.ShouldBe("bug"));
    }

    [Fact]
    public async Task WhenAuthorIsNull_MapsToEmptyString()
    {
        // Arrange
        string json = """
            {
              "data": {
                "repository": {
                  "defaultBranchRef": { "name": "main" },
                  "issues": {
                    "pageInfo": { "hasNextPage": false, "endCursor": null },
                    "nodes": [
                      {
                        "number": 7,
                        "title": "Ghost issue",
                        "body": null,
                        "url": "https://github.com/owner/repo/issues/7",
                        "state": "OPEN",
                        "author": null,
                        "labels": { "nodes": [] },
                        "blockedBy": {
                          "totalCount": 0,
                          "pageInfo": { "hasNextPage": false },
                          "nodes": []
                        }
                      }
                    ]
                  }
                }
              },
              "errors": null
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        success.Value.Listing.Issues[0].Author.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task WhenBodyIsNull_MapsToEmptyString()
    {
        // Arrange
        string json = """
            {
              "data": {
                "repository": {
                  "defaultBranchRef": { "name": "main" },
                  "issues": {
                    "pageInfo": { "hasNextPage": false, "endCursor": null },
                    "nodes": [
                      {
                        "number": 5,
                        "title": "No body",
                        "body": null,
                        "url": "https://github.com/owner/repo/issues/5",
                        "state": "OPEN",
                        "author": { "login": "dev" },
                        "labels": { "nodes": [] },
                        "blockedBy": {
                          "totalCount": 0,
                          "pageInfo": { "hasNextPage": false },
                          "nodes": []
                        }
                      }
                    ]
                  }
                }
              },
              "errors": null
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        success.Value.Listing.Issues[0].Body.ShouldBe(string.Empty);
    }

    // --- Empty node list → success with zero issues (distinct from envelope failure) ---

    [Fact]
    public async Task WhenZeroIssuesReturned_ReturnsSuccessWithEmptyListAndIsComplete()
    {
        // Arrange
        string json = """
            {
              "data": {
                "repository": {
                  "defaultBranchRef": { "name": "main" },
                  "issues": {
                    "pageInfo": { "hasNextPage": false, "endCursor": null },
                    "nodes": []
                  }
                }
              },
              "errors": null
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        success.Value.Listing.ShouldSatisfyAllConditions(
            () => success.Value.Listing.Issues.ShouldBeEmpty(),
            () => success.Value.Listing.IsComplete.ShouldBeTrue());
    }

    // --- Request shape ---

    [Fact]
    public async Task WhenCalled_PostsToGraphQlEndpoint()
    {
        // Arrange
        string json = BuildSinglePageResponse(0);
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> _ = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/graphql");
    }

    [Fact]
    public async Task WhenCalled_RequestBodyContainsRequiredGraphQlFields()
    {
        // Arrange
        string json = BuildSinglePageResponse(0);
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> _ = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        string? requestBody = handler.LastRequestBody;
        requestBody.ShouldNotBeNull();
        requestBody.ShouldSatisfyAllConditions(
            () => requestBody.ShouldContain("blockedBy"),
            () => requestBody.ShouldContain("defaultBranchRef"),
            () => requestBody.ShouldContain("rateLimit"),
            () => requestBody.ShouldContain("OPEN"),
            () => requestBody.ShouldContain("foundry"));
    }

    // --- blockedBy filter: same-repo open included, cross-repo excluded, closed excluded ---

    [Fact]
    public async Task WhenBlockedBySameRepoOpenIssue_IncludesInBlockedByMap()
    {
        // Arrange
        string json = """
            {
              "data": {
                "repository": {
                  "defaultBranchRef": { "name": "main" },
                  "issues": {
                    "pageInfo": { "hasNextPage": false, "endCursor": null },
                    "nodes": [
                      {
                        "number": 42,
                        "title": "Blocked issue",
                        "body": "",
                        "url": "https://github.com/owner/repo/issues/42",
                        "state": "OPEN",
                        "author": { "login": "dev" },
                        "labels": { "nodes": [ { "name": "foundry" } ] },
                        "blockedBy": {
                          "totalCount": 1,
                          "pageInfo": { "hasNextPage": false },
                          "nodes": [
                            {
                              "number": 10,
                              "state": "OPEN",
                              "repository": { "nameWithOwner": "owner/repo" }
                            }
                          ]
                        }
                      }
                    ]
                  }
                }
              },
              "errors": null
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        IReadOnlyDictionary<int, IReadOnlyList<int>> blockedBy = success.Value.BlockedBy;
        blockedBy.ShouldContainKey(42);
        blockedBy[42].ShouldBe([10]);
    }

    [Fact]
    public async Task WhenBlockedByCrossRepoIssue_ExcludesFromBlockedByMap()
    {
        // Arrange
        string json = """
            {
              "data": {
                "repository": {
                  "defaultBranchRef": { "name": "main" },
                  "issues": {
                    "pageInfo": { "hasNextPage": false, "endCursor": null },
                    "nodes": [
                      {
                        "number": 42,
                        "title": "Cross-repo blocked",
                        "body": "",
                        "url": "https://github.com/owner/repo/issues/42",
                        "state": "OPEN",
                        "author": { "login": "dev" },
                        "labels": { "nodes": [ { "name": "foundry" } ] },
                        "blockedBy": {
                          "totalCount": 1,
                          "pageInfo": { "hasNextPage": false },
                          "nodes": [
                            {
                              "number": 99,
                              "state": "OPEN",
                              "repository": { "nameWithOwner": "other-owner/other-repo" }
                            }
                          ]
                        }
                      }
                    ]
                  }
                }
              },
              "errors": null
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        IReadOnlyDictionary<int, IReadOnlyList<int>> blockedBy = success.Value.BlockedBy;
        blockedBy.ShouldNotContainKey(42);
    }

    [Fact]
    public async Task WhenBlockedByClosedIssue_ExcludesFromBlockedByMap()
    {
        // Arrange
        string json = """
            {
              "data": {
                "repository": {
                  "defaultBranchRef": { "name": "main" },
                  "issues": {
                    "pageInfo": { "hasNextPage": false, "endCursor": null },
                    "nodes": [
                      {
                        "number": 42,
                        "title": "Closed blocker",
                        "body": "",
                        "url": "https://github.com/owner/repo/issues/42",
                        "state": "OPEN",
                        "author": { "login": "dev" },
                        "labels": { "nodes": [ { "name": "foundry" } ] },
                        "blockedBy": {
                          "totalCount": 1,
                          "pageInfo": { "hasNextPage": false },
                          "nodes": [
                            {
                              "number": 5,
                              "state": "CLOSED",
                              "repository": { "nameWithOwner": "owner/repo" }
                            }
                          ]
                        }
                      }
                    ]
                  }
                }
              },
              "errors": null
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        IReadOnlyDictionary<int, IReadOnlyList<int>> blockedBy = success.Value.BlockedBy;
        blockedBy.ShouldNotContainKey(42);
    }

    [Fact]
    public async Task WhenBlockedByUnknownStateIssue_IncludesInBlockedByMap()
    {
        // Arrange
        string json = """
            {
              "data": {
                "repository": {
                  "defaultBranchRef": { "name": "main" },
                  "issues": {
                    "pageInfo": { "hasNextPage": false, "endCursor": null },
                    "nodes": [
                      {
                        "number": 42,
                        "title": "Unknown state blocker",
                        "body": "",
                        "url": "https://github.com/owner/repo/issues/42",
                        "state": "OPEN",
                        "author": { "login": "dev" },
                        "labels": { "nodes": [ { "name": "foundry" } ] },
                        "blockedBy": {
                          "totalCount": 1,
                          "pageInfo": { "hasNextPage": false },
                          "nodes": [
                            {
                              "number": 8,
                              "state": "DRAFT",
                              "repository": { "nameWithOwner": "owner/repo" }
                            }
                          ]
                        }
                      }
                    ]
                  }
                }
              },
              "errors": null
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        success.Value.BlockedBy.ShouldContainKey(42);
        success.Value.BlockedBy[42].ShouldBe([8]);
    }

    [Fact]
    public async Task WhenBlockedByMixedSameAndCrossRepoAndClosed_FiltersCorrectly()
    {
        // Arrange
        string json = """
            {
              "data": {
                "repository": {
                  "defaultBranchRef": { "name": "main" },
                  "issues": {
                    "pageInfo": { "hasNextPage": false, "endCursor": null },
                    "nodes": [
                      {
                        "number": 42,
                        "title": "Mixed blockers",
                        "body": "",
                        "url": "https://github.com/owner/repo/issues/42",
                        "state": "OPEN",
                        "author": { "login": "dev" },
                        "labels": { "nodes": [ { "name": "foundry" } ] },
                        "blockedBy": {
                          "totalCount": 3,
                          "pageInfo": { "hasNextPage": false },
                          "nodes": [
                            {
                              "number": 10,
                              "state": "OPEN",
                              "repository": { "nameWithOwner": "owner/repo" }
                            },
                            {
                              "number": 20,
                              "state": "CLOSED",
                              "repository": { "nameWithOwner": "owner/repo" }
                            },
                            {
                              "number": 99,
                              "state": "OPEN",
                              "repository": { "nameWithOwner": "other/repo" }
                            }
                          ]
                        }
                      }
                    ]
                  }
                }
              },
              "errors": null
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        success.Value.BlockedBy.ShouldContainKey(42);
        success.Value.BlockedBy[42].ShouldBe([10]);
    }

    [Fact]
    public async Task WhenNoBlockersAfterFiltering_IssueAbsentFromBlockedByMap()
    {
        // Arrange — issue with only a closed blocker → empty → not in map
        string json = """
            {
              "data": {
                "repository": {
                  "defaultBranchRef": { "name": "main" },
                  "issues": {
                    "pageInfo": { "hasNextPage": false, "endCursor": null },
                    "nodes": [
                      {
                        "number": 42,
                        "title": "No effective blockers",
                        "body": "",
                        "url": "https://github.com/owner/repo/issues/42",
                        "state": "OPEN",
                        "author": { "login": "dev" },
                        "labels": { "nodes": [] },
                        "blockedBy": {
                          "totalCount": 1,
                          "pageInfo": { "hasNextPage": false },
                          "nodes": [
                            {
                              "number": 5,
                              "state": "CLOSED",
                              "repository": { "nameWithOwner": "owner/repo" }
                            }
                          ]
                        }
                      }
                    ]
                  }
                }
              },
              "errors": null
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        success.Value.BlockedBy.ShouldNotContainKey(42);
    }

    // --- Non-https base URL ---

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsInvalidBaseUrlFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        GitHubHttpClient sut = BuildSut(handler);
        Uri nonHttpsUrl = new("ftp://api.github.com");

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            nonHttpsUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IssueListingWithDependencies>.Failure failure =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    // --- Envelope error handling ---

    [Fact]
    public async Task When200WithErrors_ReturnsFailureDistinctFromEmptyList()
    {
        // Arrange
        string json = """
            {
              "data": null,
              "errors": [
                { "message": "Something went wrong", "type": "INTERNAL" }
              ]
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IssueListingWithDependencies>.Failure failure =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.GraphQlError");
    }

    [Fact]
    public async Task When403WithRateLimit_ReturnsRateLimitExhausted()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "0";
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IssueListingWithDependencies>.Failure failure =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    // --- Single short page → IsComplete: true ---

    [Fact]
    public async Task WhenSingleShortPage_IssueListing_IsComplete()
    {
        // Arrange
        string json = BuildSinglePageResponse(issueCount: 3);
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        success.Value.Listing.ShouldSatisfyAllConditions(
            () => success.Value.Listing.IsComplete.ShouldBeTrue(),
            () => success.Value.Listing.Issues.Count.ShouldBe(3));
    }

    // --- Pagination: multiple full pages then short/empty → IsComplete: true ---

    [Fact]
    public async Task WhenOneFullPageThenEmptyPage_ReturnsAllIssuesAndIsComplete()
    {
        // Arrange — 100-node page with hasNextPage:true, then empty page with hasNextPage:false
        string page1 = BuildFullPageResponse(issueCount: 100, startIndex: 0, hasNextPage: true, cursor: "cursor1");
        string page2 = BuildSinglePageResponse(issueCount: 0, startIndex: 100);

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, page1),
            (HttpStatusCode.OK, page2),
        ]);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        success.Value.Listing.ShouldSatisfyAllConditions(
            () => success.Value.Listing.IsComplete.ShouldBeTrue(),
            () => success.Value.Listing.Issues.Count.ShouldBe(100));
    }

    [Fact]
    public async Task WhenTwoFullPagesThenShortPage_ReturnsAllAccumulatedIssuesAndIsComplete()
    {
        // Arrange
        string page1 = BuildFullPageResponse(issueCount: 100, startIndex: 0, hasNextPage: true, cursor: "cursor1");
        string page2 = BuildFullPageResponse(issueCount: 100, startIndex: 100, hasNextPage: true, cursor: "cursor2");
        string page3 = BuildSinglePageResponse(issueCount: 42, startIndex: 200);

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, page1),
            (HttpStatusCode.OK, page2),
            (HttpStatusCode.OK, page3),
        ]);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        success.Value.Listing.ShouldSatisfyAllConditions(
            () => success.Value.Listing.IsComplete.ShouldBeTrue(),
            () => success.Value.Listing.Issues.Count.ShouldBe(242));
    }

    [Fact]
    public async Task WhenCursorIsPassedToSubsequentPage_SecondRequestBodyContainsCursor()
    {
        // Arrange
        string page1 = BuildFullPageResponse(issueCount: 100, startIndex: 0, hasNextPage: true, cursor: "cursor-abc");
        string page2 = BuildSinglePageResponse(issueCount: 5, startIndex: 100);

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, page1),
            (HttpStatusCode.OK, page2),
        ]);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> _ = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        handler.Requests.Count.ShouldBe(2);
        string? secondRequestBody = handler.RequestBodies[1];
        secondRequestBody.ShouldNotBeNull();
        secondRequestBody.ShouldContain("cursor-abc");
    }

    // --- Page cap → IsComplete: false ---

    [Fact]
    public async Task WhenPageCapReachedWithoutShortPage_ReturnsAccumulatedIssuesWithIsCompleteFalse()
    {
        // Arrange — MaxIssuePages (20) full pages all with hasNextPage:true
        List<(HttpStatusCode, string)> responses = Enumerable
            .Range(0, 20)
            .Select(i => (HttpStatusCode.OK, BuildFullPageResponse(
                issueCount: 100,
                startIndex: i * 100,
                hasNextPage: true,
                cursor: $"cursor{i}")))
            .ToList();

        SequentialFakeHandler handler = new(responses);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        success.Value.Listing.ShouldSatisfyAllConditions(
            () => success.Value.Listing.IsComplete.ShouldBeFalse(),
            () => success.Value.Listing.Issues.Count.ShouldBe(2000));
    }

    // --- Envelope error on later page → whole result fails, no partial value ---

    [Fact]
    public async Task WhenPage2HasEnvelopeError_ReturnsFailureWithNoPartialValue()
    {
        // Arrange — page 1 succeeds, page 2 returns a GraphQL error envelope
        string page1 = BuildFullPageResponse(issueCount: 100, startIndex: 0, hasNextPage: true, cursor: "cursor1");
        string page2 = """
            {
              "data": null,
              "errors": [{ "message": "Internal server error", "type": "INTERNAL" }]
            }
            """;

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, page1),
            (HttpStatusCode.OK, page2),
        ]);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueListingWithDependencies>.Failure>();
    }

    // --- blockedBy truncation guard: paginate when hasNextPage:true ---

    [Fact]
    public async Task WhenBlockedByHasNextPage_PaginatesBlockedByUntilExhausted()
    {
        // Arrange — issue list page with one issue whose blockedBy.hasNextPage is true,
        // then a follow-up blockedBy page that exhausts the blockers.
        string issuePage = BuildIssuePageWithTruncatedBlockedBy(
            issueNumber: 42,
            initialBlockerNumber: 10,
            blockedByHasNextPage: true,
            blockedByCursor: "bb-cursor-1");

        string blockedByPage = BuildBlockedByFollowUpResponse(
            issueNumber: 42,
            blockerNumber: 11,
            hasNextPage: false);

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, issuePage),
            (HttpStatusCode.OK, blockedByPage),
        ]);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        IReadOnlyDictionary<int, IReadOnlyList<int>> blockedBy = success.Value.BlockedBy;
        blockedBy.ShouldContainKey(42);
        blockedBy[42].ShouldBe([10, 11]);
    }

    [Fact]
    public async Task WhenBlockedBySpansMultiplePages_AccumulatesAllBlockers()
    {
        // Arrange — issue list page with one issue whose blockedBy spans 3 pages
        string issuePage = BuildIssuePageWithTruncatedBlockedBy(
            issueNumber: 42,
            initialBlockerNumber: 10,
            blockedByHasNextPage: true,
            blockedByCursor: "bb-cursor-1");

        string blockedByPage2 = BuildBlockedByFollowUpResponse(
            issueNumber: 42,
            blockerNumber: 11,
            hasNextPage: true,
            cursor: "bb-cursor-2");

        string blockedByPage3 = BuildBlockedByFollowUpResponse(
            issueNumber: 42,
            blockerNumber: 12,
            hasNextPage: false);

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, issuePage),
            (HttpStatusCode.OK, blockedByPage2),
            (HttpStatusCode.OK, blockedByPage3),
        ]);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueListingWithDependencies> result = await sut.GetIssuesWithDependenciesAsync(
            ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        Result<IssueListingWithDependencies>.Success success =
            result.ShouldBeOfType<Result<IssueListingWithDependencies>.Success>();
        success.Value.BlockedBy[42].ShouldBe([10, 11, 12]);
    }

    // --- Helpers ---

    private static GitHubHttpClient BuildSut(SequentialFakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

    private static string BuildSinglePageResponse(int issueCount, int startIndex = 0)
    {
        string nodes = string.Join(",", Enumerable.Range(startIndex, issueCount).Select(i => $$"""
            {
              "number": {{i + 1}},
              "title": "Issue {{i + 1}}",
              "body": "Body {{i + 1}}",
              "url": "https://github.com/owner/repo/issues/{{i + 1}}",
              "state": "OPEN",
              "author": { "login": "user{{i}}" },
              "labels": { "nodes": [ { "name": "foundry" } ] },
              "blockedBy": { "totalCount": 0, "pageInfo": { "hasNextPage": false }, "nodes": [] }
            }
            """));

        return $$"""
            {
              "data": {
                "repository": {
                  "defaultBranchRef": { "name": "main" },
                  "issues": {
                    "pageInfo": { "hasNextPage": false, "endCursor": null },
                    "nodes": [{{nodes}}]
                  }
                }
              },
              "errors": null
            }
            """;
    }

    private static string BuildFullPageResponse(int issueCount, int startIndex, bool hasNextPage, string cursor)
    {
        string nodes = string.Join(",", Enumerable.Range(startIndex, issueCount).Select(i => $$"""
            {
              "number": {{i + 1}},
              "title": "Issue {{i + 1}}",
              "body": "Body {{i + 1}}",
              "url": "https://github.com/owner/repo/issues/{{i + 1}}",
              "state": "OPEN",
              "author": { "login": "user{{i}}" },
              "labels": { "nodes": [ { "name": "foundry" } ] },
              "blockedBy": { "totalCount": 0, "pageInfo": { "hasNextPage": false }, "nodes": [] }
            }
            """));

        string hasNextPageValue = hasNextPage ? "true" : "false";
        string cursorValue = hasNextPage ? $"\"{cursor}\"" : "null";

        return $$"""
            {
              "data": {
                "repository": {
                  "defaultBranchRef": { "name": "main" },
                  "issues": {
                    "pageInfo": { "hasNextPage": {{hasNextPageValue}}, "endCursor": {{cursorValue}} },
                    "nodes": [{{nodes}}]
                  }
                }
              },
              "errors": null
            }
            """;
    }

    private static string BuildIssuePageWithTruncatedBlockedBy(
        int issueNumber,
        int initialBlockerNumber,
        bool blockedByHasNextPage,
        string blockedByCursor)
    {
        string hasNextPageValue = blockedByHasNextPage ? "true" : "false";
        string cursorValue = blockedByHasNextPage ? $"\"{blockedByCursor}\"" : "null";

        return $$"""
            {
              "data": {
                "repository": {
                  "defaultBranchRef": { "name": "main" },
                  "issues": {
                    "pageInfo": { "hasNextPage": false, "endCursor": null },
                    "nodes": [
                      {
                        "number": {{issueNumber}},
                        "title": "Blocked issue",
                        "body": "",
                        "url": "https://github.com/owner/repo/issues/{{issueNumber}}",
                        "state": "OPEN",
                        "author": { "login": "dev" },
                        "labels": { "nodes": [ { "name": "foundry" } ] },
                        "blockedBy": {
                          "totalCount": 51,
                          "pageInfo": { "hasNextPage": {{hasNextPageValue}}, "endCursor": {{cursorValue}} },
                          "nodes": [
                            {
                              "number": {{initialBlockerNumber}},
                              "state": "OPEN",
                              "repository": { "nameWithOwner": "owner/repo" }
                            }
                          ]
                        }
                      }
                    ]
                  }
                }
              },
              "errors": null
            }
            """;
    }

    private static string BuildBlockedByFollowUpResponse(
        int issueNumber,
        int blockerNumber,
        bool hasNextPage,
        string? cursor = null)
    {
        string hasNextPageValue = hasNextPage ? "true" : "false";
        string cursorValue = cursor is not null ? $"\"{cursor}\"" : "null";

        return $$"""
            {
              "data": {
                "repository": {
                  "issue": {
                    "blockedBy": {
                      "pageInfo": { "hasNextPage": {{hasNextPageValue}}, "endCursor": {{cursorValue}} },
                      "nodes": [
                        {
                          "number": {{blockerNumber}},
                          "state": "OPEN",
                          "repository": { "nameWithOwner": "owner/repo" }
                        }
                      ]
                    }
                  }
                }
              },
              "errors": null
            }
            """;
    }
}
