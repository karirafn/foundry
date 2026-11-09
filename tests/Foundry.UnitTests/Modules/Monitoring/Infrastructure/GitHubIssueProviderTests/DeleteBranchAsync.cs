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

using Shouldly;

using Xunit;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubIssueProviderTests;

public sealed class DeleteBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubIssueProvider BuildSut(SequentialFakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);
        return new GitHubIssueProvider(
            gitHubHttpClient, new GitHubJobLogClient(httpClient), new ActionableFeedbackPolicy(TimeProvider.System), "ghp_token", ValidBaseUrl);
    }

    [Fact]
    public async Task WhenBranchIsAWorkBranch_DeletesIt()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, """{ "default_branch": "main" }"""),
            (HttpStatusCode.NoContent, string.Empty),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

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
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, """{ "default_branch": "main" }""")]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await sut.DeleteBranchAsync(ValidSlug, "main", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<bool>.Failure>().Error.Code.ShouldBe(BranchDeletionErrors.DefaultBranchCode);
        handler.Requests.ShouldNotContain(r => r.Method == HttpMethod.Delete);
    }
}
