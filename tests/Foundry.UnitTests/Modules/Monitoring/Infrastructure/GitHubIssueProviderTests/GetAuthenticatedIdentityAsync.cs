using System.Net;

using Foundry.Modules.Monitoring.Contracts.Commands;
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

public sealed class GetAuthenticatedIdentityAsync
{
    private const string ValidToken = "ghp_token";

    private static GitHubIssueProvider BuildSut(HttpMessageHandler handler, string apiBaseUrl = "https://api.github.com")
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
            new Uri(apiBaseUrl));
    }

    [Fact]
    public async Task WhenTheUserHasAName_ReturnsTheNameAndTheNoreplyAddress()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, """{ "id": 583231, "login": "octocat", "name": "The Octocat" }""");
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<CommitIdentity> result = await sut.GetAuthenticatedIdentityAsync(CancellationToken.None);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ValueOrThrow().ShouldBe(
                new CommitIdentity("The Octocat", "583231+octocat@users.noreply.github.com")),
            () => handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/user"),
            () => handler.LastRequest!.Headers.Authorization!.Parameter.ShouldBe(ValidToken));
    }

    [Theory]
    [InlineData("""{ "id": 583231, "login": "octocat", "name": null }""")]
    [InlineData("""{ "id": 583231, "login": "octocat", "name": "  " }""")]
    public async Task WhenTheUserHasNoName_UsesTheLogin(string userJson)
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, userJson);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<CommitIdentity> result = await sut.GetAuthenticatedIdentityAsync(CancellationToken.None);

        // Assert
        result.ValueOrThrow().Name.ShouldBe("octocat");
    }

    [Fact]
    public async Task WhenTheAccountIsOnAnEnterpriseServer_UsesThatHostsNoreplyDomain()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, """{ "id": 7, "login": "dev", "name": "Dev" }""");
        GitHubIssueProvider sut = BuildSut(handler, "https://ghe.example.com/api/v3");

        // Act
        Result<CommitIdentity> result = await sut.GetAuthenticatedIdentityAsync(CancellationToken.None);

        // Assert
        result.ValueOrThrow().Email.ShouldBe("7+dev@users.noreply.ghe.example.com");
    }

    [Fact]
    public async Task WhenTheResponseLacksTheIdOrLogin_FailsPermanent()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, """{ "name": "The Octocat" }""");
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<CommitIdentity> result = await sut.GetAuthenticatedIdentityAsync(CancellationToken.None);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, RunPublishingErrors.TransientCode)]
    [InlineData(HttpStatusCode.TooManyRequests, RunPublishingErrors.TransientCode)]
    [InlineData(HttpStatusCode.Unauthorized, RunPublishingErrors.PermanentCode)]
    public async Task WhenTheProviderFails_ClassifiesTheFailure(HttpStatusCode statusCode, string expectedCode)
    {
        // Arrange
        FakeHandler handler = new(statusCode, "{}");
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<CommitIdentity> result = await sut.GetAuthenticatedIdentityAsync(CancellationToken.None);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(expectedCode);
    }

    [Fact]
    public async Task WhenTheConnectionFails_FailsTransient()
    {
        // Arrange
        ThrowingHandler handler = new(new HttpRequestException("connection reset"));
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<CommitIdentity> result = await sut.GetAuthenticatedIdentityAsync(CancellationToken.None);

        // Assert
        RunPublishingErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }
}
