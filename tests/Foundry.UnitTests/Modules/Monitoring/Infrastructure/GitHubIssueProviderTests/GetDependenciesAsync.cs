using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubIssueProviderTests;

public sealed class GetDependenciesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubIssueProvider BuildSut(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);
        return new GitHubIssueProvider(
            gitHubHttpClient, new ActionableFeedbackPolicy(TimeProvider.System), ValidToken, ValidBaseUrl);
    }

    // --- After GetIssuesAsync, GetDependenciesAsync returns cached blockers with zero extra HTTP requests ---

    [Fact]
    public async Task AfterGetIssuesAsync_GetDependenciesAsync_ReturnsCachedBlockers()
    {
        // Arrange — GraphQL response with one issue blocked by issue 10
        string issueListJson = """
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

        FakeHandler handler = new(HttpStatusCode.OK, issueListJson);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<IssueListing> issueResult = await sut.GetIssuesAsync(ValidSlug, CancellationToken.None);
        int requestCountAfterGetIssues = handler.AllRequests.Count;
        Result<IReadOnlyList<int>> depsResult = await sut.GetDependenciesAsync(ValidSlug, 42, CancellationToken.None);

        // Assert
        issueResult.IsSuccess.ShouldBeTrue();
        depsResult.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success depsSuccess = depsResult.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        depsSuccess.Value.ShouldBe([10]);
        handler.AllRequests.Count.ShouldBe(requestCountAfterGetIssues, "no additional HTTP requests should be made");
    }

    [Fact]
    public async Task AfterGetIssuesAsync_WhenIssueNumberUnknown_ReturnsEmpty()
    {
        // Arrange — GraphQL response with issue 42 having no blockers
        string issueListJson = """
            {
              "data": {
                "repository": {
                  "defaultBranchRef": { "name": "main" },
                  "issues": {
                    "pageInfo": { "hasNextPage": false, "endCursor": null },
                    "nodes": [
                      {
                        "number": 42,
                        "title": "Unblocked issue",
                        "body": "",
                        "url": "https://github.com/owner/repo/issues/42",
                        "state": "OPEN",
                        "author": { "login": "dev" },
                        "labels": { "nodes": [ { "name": "foundry" } ] },
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

        FakeHandler handler = new(HttpStatusCode.OK, issueListJson);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<IssueListing> _ = await sut.GetIssuesAsync(ValidSlug, CancellationToken.None);
        int requestCountAfterGetIssues = handler.AllRequests.Count;
        Result<IReadOnlyList<int>> depsResult = await sut.GetDependenciesAsync(ValidSlug, 999, CancellationToken.None);

        // Assert
        depsResult.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success depsSuccess = depsResult.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        depsSuccess.Value.ShouldBeEmpty();
        handler.AllRequests.Count.ShouldBe(requestCountAfterGetIssues, "no additional HTTP requests should be made");
    }

    [Fact]
    public async Task AfterGetIssuesAsync_WhenIssueHasNoBlockers_ReturnsEmpty()
    {
        // Arrange — issue 42 fetched but had all blockers filtered (cross-repo/closed)
        string issueListJson = """
            {
              "data": {
                "repository": {
                  "defaultBranchRef": { "name": "main" },
                  "issues": {
                    "pageInfo": { "hasNextPage": false, "endCursor": null },
                    "nodes": [
                      {
                        "number": 42,
                        "title": "Issue",
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

        FakeHandler handler = new(HttpStatusCode.OK, issueListJson);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<IssueListing> _ = await sut.GetIssuesAsync(ValidSlug, CancellationToken.None);
        int requestCountAfterGetIssues = handler.AllRequests.Count;
        Result<IReadOnlyList<int>> depsResult = await sut.GetDependenciesAsync(ValidSlug, 42, CancellationToken.None);

        // Assert
        depsResult.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success depsSuccess = depsResult.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        depsSuccess.Value.ShouldBeEmpty();
        handler.AllRequests.Count.ShouldBe(requestCountAfterGetIssues, "no additional HTTP requests should be made");
    }

    // --- Map null (no prior GetIssuesAsync) → REST fallback fires ---

    [Fact]
    public async Task WhenGetIssuesAsyncNotYetCalled_FallsBackToRestEndpoint()
    {
        // Arrange — REST dependency response (the fallback path)
        string restDepsJson = """
            [
              {
                "number": 10,
                "state": "open",
                "repository": { "full_name": "owner/repo" }
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, restDepsJson);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act — call GetDependenciesAsync without calling GetIssuesAsync first
        Result<IReadOnlyList<int>> depsResult = await sut.GetDependenciesAsync(ValidSlug, 10, CancellationToken.None);

        // Assert
        depsResult.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success depsSuccess = depsResult.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        depsSuccess.Value.ShouldBe([10]);
        handler.AllRequests.Count.ShouldBe(1, "REST fallback should fire exactly one HTTP request");
    }
}
