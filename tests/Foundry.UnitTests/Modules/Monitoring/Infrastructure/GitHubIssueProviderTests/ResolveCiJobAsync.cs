using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
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

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubIssueProviderTests;

public sealed class ResolveCiJobAsync
{
    private static readonly Uri ApiBaseUrl = new("https://api.github.com");

    // Shape of "List jobs for a workflow run": the check run behind job 222 has its own, different ID (999).
    private const string RunJobsJson = """
        {
          "total_count": 2,
          "jobs": [
            {
              "id": 111,
              "run_id": 55,
              "name": "lint",
              "html_url": "https://github.com/owner/repo/actions/runs/55/job/111",
              "check_run_url": "https://api.github.com/repos/owner/repo/check-runs/888"
            },
            {
              "id": 222,
              "run_id": 55,
              "name": "build (ubuntu)",
              "html_url": "https://github.com/owner/repo/actions/runs/55/job/222",
              "check_run_url": "https://api.github.com/repos/owner/repo/check-runs/999"
            }
          ]
        }
        """;

    private static RepositorySlug Slug => RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubIssueProvider BuildSut(FakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);
        return new GitHubIssueProvider(
            gitHubHttpClient,
            new GitHubJobLogClient(httpClient),
            new ActionableFeedbackPolicy(TimeProvider.System),
            "ghp_token",
            ApiBaseUrl);
    }

    private static async Task<CiJobLookup> ResolveAsync(FakeHandler handler, FailingCheck check)
    {
        Result<CiJobLookup> result = await BuildSut(handler).ResolveCiJobAsync(
            Slug, check, TestContext.Current.CancellationToken);
        return result.ShouldBeOfType<Result<CiJobLookup>.Success>().Value;
    }

    [Fact]
    public async Task WhenTheCheckLinksToAnActionsJob_ResolvesTheJobWhoseHtmlUrlMatchesFromTheRunsJobs()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, RunJobsJson);
        FailingCheck check = new("build (ubuntu)", "failure", "https://github.com/owner/repo/actions/runs/55/job/222?pr=7");

        // Act
        CiJobLookup lookup = await ResolveAsync(handler, check);

        // Assert
        lookup.ShouldBe(new CiJobLookup.Found(222));
        handler.LastRequest!.RequestUri!.PathAndQuery
            .ShouldBe("/repos/owner/repo/actions/runs/55/jobs?filter=latest&per_page=100");
    }

    [Fact]
    public async Task WhenNoJobPageMatches_ResolvesTheSingleJobWithTheChecksName()
    {
        // Arrange — the details URL names a job ID absent from the latest attempt (e.g. a re-run).
        FakeHandler handler = new(HttpStatusCode.OK, RunJobsJson);
        FailingCheck check = new("lint", "failure", "https://github.com/owner/repo/actions/runs/55/job/100");

        // Act
        CiJobLookup lookup = await ResolveAsync(handler, check);

        // Assert
        lookup.ShouldBe(new CiJobLookup.Found(111));
    }

    [Fact]
    public async Task WhenNoJobOfTheRunMatches_IsNotACiJob()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, RunJobsJson);
        FailingCheck check = new("deploy", "failure", "https://github.com/owner/repo/actions/runs/55/job/100");

        // Act
        CiJobLookup lookup = await ResolveAsync(handler, check);

        // Assert
        lookup.ShouldBeOfType<CiJobLookup.NotACiJob>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://ci.example.com/build/42")]
    [InlineData("https://github.com/owner/repo/runs/999")]
    [InlineData("https://github.com/other/repo/actions/runs/55/job/222")]
    public async Task WhenTheCheckIsNotAnActionsJobOfThisRepository_IsNotACiJobWithoutAnyRequest(string? detailsUrl)
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, RunJobsJson);
        FailingCheck check = new("ci/status", "failure", detailsUrl);

        // Act
        CiJobLookup lookup = await ResolveAsync(handler, check);

        // Assert
        lookup.ShouldBeOfType<CiJobLookup.NotACiJob>();
        handler.AllRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenListingTheRunsJobsFails_Fails()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, "{}");
        FailingCheck check = new("lint", "failure", "https://github.com/owner/repo/actions/runs/55/job/111");

        // Act
        Result<CiJobLookup> result = await BuildSut(handler).ResolveCiJobAsync(
            Slug, check, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<CiJobLookup>.Failure>();
    }
}
