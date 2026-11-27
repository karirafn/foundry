using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.GitHubChecksTests;

public sealed class GetChecksAsync
{
    // rateLimit block from the 2026-11-27 `gh api graphql` capture on karirafn/foundry.
    private const string CapturedRateLimit = """{"cost":1,"remaining":4990,"limit":5000,"resetAt":"2026-11-27T10:23:06Z"}""";

    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidPullRequestUrl = "https://github.com/owner/repo/pull/42";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(FakeHandler handler, InMemoryProviderRateBudget? budget = null) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            budget ?? new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenRollupStateIsSuccess_ReturnsGreen()
    {
        // Arrange
        string json = $$"""
            {
              "data": {
                "rateLimit":{{CapturedRateLimit}},
                "repository": {
                  "pullRequest": {
                    "commits": {
                      "nodes": [
                        {
                          "commit": {
                            "statusCheckRollup": {
                              "state": "SUCCESS",
                              "contexts": { "nodes": [] }
                            }
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidPullRequestUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Green>();
    }

    [Fact]
    public async Task WhenRollupStateIsFailure_ReturnsRedWithFailingChecks()
    {
        // Arrange
        // The FAILURE conclusion is the token GitHub returns, as in the captured response of the test below.
        string json = $$"""
            {
              "data": {
                "rateLimit":{{CapturedRateLimit}},
                "repository": {
                  "pullRequest": {
                    "commits": {
                      "nodes": [
                        {
                          "commit": {
                            "statusCheckRollup": {
                              "state": "FAILURE",
                              "contexts": {
                                "nodes": [
                                  {
                                    "__typename": "CheckRun",
                                    "name": "ci / build",
                                    "conclusion": "FAILURE",
                                    "detailsUrl": "https://github.com/owner/repo/actions/runs/1"
                                  }
                                ]
                              }
                            }
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidPullRequestUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.Count.ShouldBe(1);
        red.FailingChecks[0].ShouldSatisfyAllConditions(
            () => red.FailingChecks[0].Name.ShouldBe("ci / build"),
            () => red.FailingChecks[0].Conclusion.ShouldBe("FAILURE"),
            () => red.FailingChecks[0].DetailsUrl.ShouldBe("https://github.com/owner/repo/actions/runs/1"),
            () => red.FailingChecks[0].IsInfrastructureFailure.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenRollupIsTheCapturedGitHubResponse_NamesOnlyTheFailedCheckRun()
    {
        // Arrange
        // The rollup is captured verbatim from GitHub's GraphQL API: pullRequest.commits(last:1).nodes.commit.statusCheckRollup
        // on a pull request in this repository, 2026-11-12, selecting __typename,
        // CheckRun { name conclusion } and StatusContext { context state }.
        string json = $$"""
            {
              "data": {
                "rateLimit":{{CapturedRateLimit}},
                "repository": {
                  "pullRequest": {
                    "commits": {
                      "nodes": [
                        {
                          "commit": {
                            "statusCheckRollup": {"contexts":{"nodes":[
                              {"__typename":"CheckRun","conclusion":"SKIPPED","name":"auto-merge"},
                              {"__typename":"CheckRun","conclusion":"SUCCESS","name":"api"},
                              {"__typename":"CheckRun","conclusion":"FAILURE","name":"web"},
                              {"__typename":"CheckRun","conclusion":"SUCCESS","name":"workers"}
                            ]},"state":"FAILURE"}
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidPullRequestUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.Select(check => (check.Name, check.Conclusion)).ShouldBe([("web", "FAILURE")]);
    }

    [Fact]
    public async Task WhenRollupStateIsPending_ReturnsPending()
    {
        // Arrange
        string json = $$"""
            {
              "data": {
                "rateLimit":{{CapturedRateLimit}},
                "repository": {
                  "pullRequest": {
                    "commits": {
                      "nodes": [
                        {
                          "commit": {
                            "statusCheckRollup": {
                              "state": "PENDING",
                              "contexts": { "nodes": [] }
                            }
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidPullRequestUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Pending>();
    }

    [Fact]
    public async Task WhenRollupIsAbsent_ReturnsNoneDetected()
    {
        // Arrange — PR has no statusCheckRollup
        string json = $$"""
            {
              "data": {
                "rateLimit":{{CapturedRateLimit}},
                "repository": {
                  "pullRequest": {
                    "commits": {
                      "nodes": [
                        {
                          "commit": {
                            "statusCheckRollup": null
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidPullRequestUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.NoneDetected>();
    }

    [Fact]
    public async Task WhenGitHubReturns401_ReturnsUnreadable()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Unauthorized, string.Empty);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidPullRequestUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert — 401 maps to Unreadable, not a Result failure
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Unreadable>();
    }

    [Fact]
    public async Task WhenGitHubReturns403WithoutRateLimitHeader_ReturnsUnreadable()
    {
        // Arrange — 403 without a zero X-RateLimit-Remaining is an auth failure, not rate-limit
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidPullRequestUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Unreadable>();
    }

    [Fact]
    public async Task WhenGitHubReturns403WithRateLimitExhausted_ReturnsRateLimitFailure()
    {
        // Arrange — 403 with X-RateLimit-Remaining: 0 is a rate-limit failure, not Unreadable
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "0";
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidPullRequestUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<ProviderChecks>.Failure failure = result.ShouldBeOfType<Result<ProviderChecks>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenRollupHasStatusContextInErrorState_ReturnsRedWithContextInFailingChecks()
    {
        // Arrange
        // The ERROR token is a StatusState value: https://docs.github.com/en/graphql/reference/commits (StatusState).
        string json = $$"""
            {
              "data": {
                "rateLimit":{{CapturedRateLimit}},
                "repository": {
                  "pullRequest": {
                    "commits": {
                      "nodes": [
                        {
                          "commit": {
                            "statusCheckRollup": {
                              "state": "ERROR",
                              "contexts": {
                                "nodes": [
                                  {
                                    "__typename": "StatusContext",
                                    "context": "ci/jenkins",
                                    "state": "ERROR",
                                    "targetUrl": "https://jenkins.example.com/job/1"
                                  }
                                ]
                              }
                            }
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidPullRequestUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.Count.ShouldBe(1);
        red.FailingChecks[0].ShouldSatisfyAllConditions(
            () => red.FailingChecks[0].Name.ShouldBe("ci/jenkins"),
            () => red.FailingChecks[0].Conclusion.ShouldBe("ERROR"),
            () => red.FailingChecks[0].DetailsUrl.ShouldBe("https://jenkins.example.com/job/1"),
            () => red.FailingChecks[0].IsInfrastructureFailure.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenACheckRunFailedToStartBesideAFailedStatusContext_MarksOnlyTheCheckRunAnInfrastructureFailure()
    {
        // Arrange
        // STARTUP_FAILURE is a CheckConclusionState value: https://docs.github.com/en/graphql/reference/checks;
        // FAILURE is a StatusState value: https://docs.github.com/en/graphql/reference/commits (StatusState).
        string json = $$"""
            {
              "data": {
                "rateLimit":{{CapturedRateLimit}},
                "repository": {
                  "pullRequest": {
                    "commits": {
                      "nodes": [
                        {
                          "commit": {
                            "statusCheckRollup": {
                              "state": "FAILURE",
                              "contexts": {
                                "nodes": [
                                  {
                                    "__typename": "CheckRun",
                                    "name": "ci / build",
                                    "conclusion": "STARTUP_FAILURE",
                                    "detailsUrl": "https://github.com/owner/repo/actions/runs/1"
                                  },
                                  {
                                    "__typename": "StatusContext",
                                    "context": "ci/jenkins",
                                    "state": "FAILURE",
                                    "targetUrl": "https://jenkins.example.com/job/1"
                                  }
                                ]
                              }
                            }
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, json));

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidPullRequestUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe(
            [
                new FailingCheck("ci / build", "STARTUP_FAILURE", "https://github.com/owner/repo/actions/runs/1", IsInfrastructureFailure: true),
                new FailingCheck("ci/jenkins", "FAILURE", "https://jenkins.example.com/job/1"),
            ]);
    }

    [Fact]
    public async Task WhenReading_RecordsTheGraphQlHeadroomFromTheData()
    {
        // Arrange
        string json = $$"""
            {
              "data": {
                "rateLimit":{{CapturedRateLimit}},
                "repository": {
                  "pullRequest": {
                    "commits": {
                      "nodes": [
                        {
                          "commit": {
                            "statusCheckRollup": {
                              "state": "SUCCESS",
                              "contexts": { "nodes": [] }
                            }
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;
        InMemoryProviderRateBudget budget = new();
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, json), budget);

        // Act
        await sut.GetChecksAsync(ValidBaseUrl, ValidSlug, ValidPullRequestUrl, token: "ghp_token", CancellationToken.None);

        // Assert
        RateBudgetReading reading = budget.TryGet(ProviderBudgetKey.GitHubGraphQl).ShouldNotBeNull();
        reading.ShouldSatisfyAllConditions(
            () => reading.Remaining.ShouldBe(4990),
            () => reading.Limit.ShouldBe(5000),
            () => reading.ResetAt.ShouldBe(new DateTimeOffset(2026, 11, 27, 10, 23, 6, TimeSpan.Zero)));
    }
}
