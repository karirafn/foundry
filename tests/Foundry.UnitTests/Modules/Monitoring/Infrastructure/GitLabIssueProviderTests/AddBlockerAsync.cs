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

public sealed class AddBlockerAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat-token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabIssueProvider BuildSut(DelegatingHandler handler)
    {
        GitLabHttpClient gitLabHttpClient = new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);
        return new GitLabIssueProvider(
            gitLabHttpClient, new ActionableFeedbackPolicy(TimeProvider.System), ValidToken, ValidBaseUrl);
    }

    [Fact]
    public async Task WhenGitLabRecordsTheBlockingLink_PostsItToTheIssueAndSucceeds()
    {
        // Arrange
        // The project lookup carries the project_id the docs.gitlab.com/api/issue_links examples use (4); the
        // links are derived from the docs list example, reduced to the fields read, with iid 15 blocking.
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, """{"id":4,"default_branch":"main"}"""),
            (HttpStatusCode.Created, """{"link_type":"is_blocked_by"}"""),
            (HttpStatusCode.OK, """[{"iid":15,"project_id":4,"state":"opened","link_type":"is_blocked_by"}]"""),
        ]);
        IIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await sut.AddBlockerAsync(ValidSlug, 42, 15, CancellationToken.None);

        // Assert
        handler.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Success>(),
            () => handler.Requests[1].RequestUri!.AbsolutePath
                .ShouldBe("/api/v4/projects/group%2Fproject/issues/42/links"));
    }
}
