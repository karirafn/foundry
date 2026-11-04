using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabIssueProviderTests;

public sealed class DeleteBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabIssueProvider BuildSut(SequentialFakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitLabHttpClient gitLabHttpClient = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);
        return new GitLabIssueProvider(
            gitLabHttpClient, new ActionableFeedbackPolicy(TimeProvider.System), "glpat_token", ValidBaseUrl);
    }

    [Fact]
    public async Task WhenBranchIsAWorkBranch_DeletesIt()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, """{ "id": 1, "default_branch": "main" }"""),
            (HttpStatusCode.NoContent, string.Empty),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await sut.DeleteBranchAsync(ValidSlug, "feat/42-add-retry", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<bool>.Success>().Value.ShouldBeTrue();
        handler.Requests[^1].Method.ShouldBe(HttpMethod.Delete);
    }

    [Fact]
    public async Task WhenBranchIsTheDefaultBranch_RefusesWithoutDeleting()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, """{ "id": 1, "default_branch": "main" }""")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await sut.DeleteBranchAsync(ValidSlug, "main", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<bool>.Failure>().Error.Code.ShouldBe(BranchDeletionErrors.DefaultBranchCode);
        handler.Requests.ShouldNotContain(r => r.Method == HttpMethod.Delete);
    }
}
