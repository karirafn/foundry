using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
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

public sealed class GetPullRequestBodyAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    // Captured 2026-11-20 from GET projects/gitlab-org%2Fgitlab/merge_requests/1 on gitlab.com, cut to six fields and
    // the description to 60 characters.
    private const string Captured = """
        {"id":83382,"iid":1,"project_id":278964,"title":"Enhance the LDAP group synchronization","state":"merged","description":"The response of our company's LDAP for a group looks like th"}
        """;

    private static IIssueProvider BuildSut(DelegatingHandler handler)
    {
        GitLabHttpClient gitLabHttpClient = new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);
        return new GitLabIssueProvider(
            gitLabHttpClient, new ActionableFeedbackPolicy(TimeProvider.System), "token", ValidBaseUrl);
    }

    [Fact]
    public async Task WhenTheMergeRequestHasADescription_ReturnsIt()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, Captured);
        IIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await sut.GetPullRequestBodyAsync(
            RepositorySlug.Create("gitlab-org/gitlab").ValueOrThrow(),
            "https://gitlab.com/gitlab-org/gitlab/-/merge_requests/1",
            CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe("The response of our company's LDAP for a group looks like th");
        handler.LastRequest.ShouldNotBeNull().RequestUri.ShouldNotBeNull().AbsolutePath
            .ShouldBe("/api/v4/projects/gitlab-org%2Fgitlab/merge_requests/1");
    }
}
