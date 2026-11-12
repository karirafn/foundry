using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Providers.TrackerReads;
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

public sealed class TrackerReads
{
    private const string Token = "ghp_provider_token";

    private static readonly Uri BaseUrl = new("https://api.github.com");

    private static RepositorySlug Slug => RepositorySlug.Create("karirafn/foundry").ValueOrThrow();

    private static GitHubIssueProvider BuildSut(HttpMessageHandler handler)
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
            Token,
            BaseUrl);
    }

    [Fact]
    public async Task GetIssueLinksAsync_ReadsTheLinksWithTheProvidersTokenAndBaseUrl()
    {
        // Arrange
        // Captured 2026-11-12: repos/karirafn/foundry/issues/865/dependencies/blocking?per_page=100, trimmed.
        string captured = """
            [{"number":848,"state":"open","title":"feat(workers): runs receive the issues their issue reference","repository":{"full_name":"karirafn/foundry"}}]
            """;
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, "[]")
            .WithRoute("/dependencies/blocking", HttpStatusCode.OK, captured);
        IIssueProvider sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<IssueReference>> result = await sut.GetIssueLinksAsync(Slug, 865, CancellationToken.None);

        // Assert
        IReadOnlyList<IssueReference> links = result.ShouldBeOfType<Result<IReadOnlyList<IssueReference>>.Success>().Value;
        links.Select(link => (link.Number, link.Relation)).ShouldBe([(848, IssueRelation.Blocks)]);
        handler.LastRequest!.Headers.Authorization!.Parameter.ShouldBe(Token);
    }

    [Fact]
    public async Task GetOpenIssuesAsync_ReadsTheIndexWithTheProvidersTokenAndBaseUrl()
    {
        // Arrange
        // Captured 2026-11-12 (issues(first: 3, ...) on karirafn/foundry), cut to its first node.
        string captured = """
            {"data":{"repository":{"issues":{"pageInfo":{"hasNextPage":true,"endCursor":"Y3Vyc29yOnYyOpK0MjAyNi0xMS0xMlQxNToyMjowMFrPAAAAAXqq0iI="},"nodes":[{"number":867,"title":"feat(monitoring): GitLab names a failed pipeline's failed jobs as failing checks"}]}}}}
            """;
        FakeHandler handler = new(HttpStatusCode.OK, captured);
        IIssueProvider sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<OpenIssue>> result = await sut.GetOpenIssuesAsync(Slug, CancellationToken.None);

        // Assert
        IReadOnlyList<OpenIssue> issues = result.ShouldBeOfType<Result<IReadOnlyList<OpenIssue>>.Success>().Value;
        issues.Select(issue => issue.Number).ShouldBe([867]);
        handler.LastRequest!.Headers.Authorization!.Parameter.ShouldBe(Token);
    }
}
