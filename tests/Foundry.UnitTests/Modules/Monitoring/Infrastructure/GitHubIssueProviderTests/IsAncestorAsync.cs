using System.Net;

using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
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

public sealed class IsAncestorAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

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
            ValidToken,
            ValidBaseUrl);
    }

    private static Task<Result<bool>> CompareAsync(GitHubIssueProvider sut) =>
        sut.IsAncestorAsync(ValidSlug, "feat/42-add-retry-run-7", "feat/42-add-retry", CancellationToken.None);

    [Theory]
    [InlineData("ahead", true)]
    [InlineData("identical", true)]
    [InlineData("behind", false)]
    [InlineData("diverged", false)]
    public async Task WhenCompared_AnswersFromTheCompareStatus(string status, bool expected)
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, $$"""{ "status": "{{status}}", "ahead_by": 1, "behind_by": 0 }""");
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await CompareAsync(sut);

        // Assert
        result.ValueOrThrow().ShouldBe(expected);
    }

    [Fact]
    public async Task WhenCompared_ComparesTheAncestorAsBaseAndTheBranchAsHead()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, """{ "status": "ahead" }""");
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        await CompareAsync(sut);

        // Assert
        handler.ShouldSatisfyAllConditions(
            () => handler.LastRequest!.Method.ShouldBe(HttpMethod.Get),
            () => handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe(
                "/repos/owner/repo/compare/feat%2F42-add-retry-run-7...feat%2F42-add-retry"),
            () => handler.LastRequest!.Headers.Authorization!.Parameter.ShouldBe(ValidToken));
    }

    [Fact]
    public async Task WhenEitherRefIsMissing_FailsNotFound()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, """{ "message": "Not Found" }""");
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await CompareAsync(sut);

        // Assert
        Error error = result.ErrorOrThrow();
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(RunPublishingErrors.RefNotFoundCode),
            () => error.Kind.ShouldBe(ErrorKind.NotFound));
    }

    [Fact]
    public async Task WhenTheStatusIsUnknown_FailsPermanent()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, """{ "ahead_by": 1 }""");
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await CompareAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode);
    }

    [Fact]
    public async Task WhenTheProviderFailsWithAServerError_FailsTransient()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.BadGateway, "{}");
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await CompareAsync(sut);

        // Assert
        RunPublishingErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }
}
