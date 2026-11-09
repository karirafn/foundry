using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
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

public sealed class ResolveCiJobAsync
{
    private static RepositorySlug Slug => RepositorySlug.Create("group/sub/project").ValueOrThrow();

    private static async Task<(CiJobLookup Lookup, FakeHandler Handler)> ResolveAsync(string? detailsUrl)
    {
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        GitLabIssueProvider sut = new(
            new GitLabHttpClient(
                new HttpClient(handler),
                NullLogger<GitLabHttpClient>.Instance,
                new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
                new InMemoryProviderRateBudget(),
                TimeProvider.System),
            new ActionableFeedbackPolicy(TimeProvider.System),
            "glpat-token",
            new Uri("https://gitlab.com/api/v4"));

        Result<CiJobLookup> result = await sut.ResolveCiJobAsync(
            Slug,
            new FailingCheck("test", "failed", detailsUrl),
            TestContext.Current.CancellationToken);
        return (result.ShouldBeOfType<Result<CiJobLookup>.Success>().Value, handler);
    }

    [Fact]
    public async Task WhenTheCheckLinksToAJobOfThisProject_ResolvesThatJobWithoutAnyRequest()
    {
        // Act
        (CiJobLookup lookup, FakeHandler handler) = await ResolveAsync("https://gitlab.com/group/sub/project/-/jobs/4242");

        // Assert
        lookup.ShouldBe(new CiJobLookup.Found(4242));
        handler.AllRequests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://gitlab.com/group/sub/project/-/pipelines/12")]
    [InlineData("https://gitlab.com/group/other/-/jobs/4242")]
    [InlineData("https://sonar.example.com/dashboard")]
    public async Task WhenTheCheckIsNotAJobOfThisProject_IsNotACiJob(string? detailsUrl)
    {
        // Act
        (CiJobLookup lookup, _) = await ResolveAsync(detailsUrl);

        // Assert
        lookup.ShouldBeOfType<CiJobLookup.NotACiJob>();
    }
}
