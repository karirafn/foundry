using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
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

public sealed class GetPullRequestBodyAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    // Captured 2026-11-20 from GET repos/karirafn/foundry/pulls/1218, cut to four fields and the body to 60 characters.
    private const string Captured = """
        {"number":1218,"state":"closed","title":"feat: A review criterion covered by passing checks counts as met","body":"> [!WARNING]\n> - Once a verdict with covered_by_checks is st"}
        """;

    private static IIssueProvider BuildSut(DelegatingHandler handler)
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
            "token",
            ValidBaseUrl);
    }

    [Fact]
    public async Task WhenThePullRequestHasABody_ReturnsIt()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, Captured);
        IIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await sut.GetPullRequestBodyAsync(
            RepositorySlug.Create("karirafn/foundry").ValueOrThrow(),
            "https://github.com/karirafn/foundry/pull/1218",
            CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe("> [!WARNING]\n> - Once a verdict with covered_by_checks is st");
        handler.LastRequest.ShouldNotBeNull().RequestUri.ShouldNotBeNull().AbsolutePath
            .ShouldBe("/repos/karirafn/foundry/pulls/1218");
    }
}
