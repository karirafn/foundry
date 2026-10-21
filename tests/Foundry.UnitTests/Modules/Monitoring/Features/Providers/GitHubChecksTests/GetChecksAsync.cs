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
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidPullRequestUrl = "https://github.com/owner/repo/pull/42";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(FakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenRollupStateIsSuccess_ReturnsGreen()
    {
        // Arrange
        string json = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
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
        string json = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
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
                                    "conclusion": "failure",
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
            () => red.FailingChecks[0].Conclusion.ShouldBe("failure"),
            () => red.FailingChecks[0].DetailsUrl.ShouldBe("https://github.com/owner/repo/actions/runs/1"));
    }

    [Fact]
    public async Task WhenRollupStateIsPending_ReturnsPending()
    {
        // Arrange
        string json = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
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
        string json = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
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
    public async Task WhenRollupHasStatusContextFailure_ReturnsRedWithContextInFailingChecks()
    {
        // Arrange
        string json = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
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
                                    "__typename": "StatusContext",
                                    "context": "ci/jenkins",
                                    "state": "failure",
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
            () => red.FailingChecks[0].Conclusion.ShouldBe("failure"),
            () => red.FailingChecks[0].DetailsUrl.ShouldBe("https://jenkins.example.com/job/1"));
    }
}
