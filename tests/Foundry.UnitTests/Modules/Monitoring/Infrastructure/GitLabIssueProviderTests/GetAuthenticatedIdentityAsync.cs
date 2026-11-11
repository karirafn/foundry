using System.Net;

using Foundry.Modules.Monitoring.Contracts.Commands;
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

public sealed class GetAuthenticatedIdentityAsync
{
    private const string ValidToken = "glpat_token";

    private static GitLabIssueProvider BuildSut(HttpMessageHandler handler, string apiBaseUrl = "https://gitlab.com/api/v4")
    {
        GitLabHttpClient gitLabHttpClient = new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);
        return new GitLabIssueProvider(
            gitLabHttpClient, new ActionableFeedbackPolicy(TimeProvider.System), ValidToken, new Uri(apiBaseUrl));
    }

    [Fact]
    public async Task WhenTheUserHasACommitEmail_ReturnsTheNameAndCommitEmail()
    {
        // Arrange
        FakeHandler handler = new(
            HttpStatusCode.OK,
            """{ "id": 31, "username": "jdoe", "name": "Jane Doe", "commit_email": "jane@work.example", "public_email": "jane@public.example" }""");
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<CommitIdentity> result = await sut.GetAuthenticatedIdentityAsync(CancellationToken.None);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ValueOrThrow().ShouldBe(new CommitIdentity("Jane Doe", "jane@work.example")),
            () => handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/api/v4/user"),
            () => handler.LastRequest!.Headers.GetValues("PRIVATE-TOKEN").ShouldBe([ValidToken]));
    }

    [Fact]
    public async Task WhenTheUserHasOnlyAPublicEmail_UsesThePublicEmail()
    {
        // Arrange
        FakeHandler handler = new(
            HttpStatusCode.OK,
            """{ "id": 31, "username": "jdoe", "name": "Jane Doe", "commit_email": "", "public_email": "jane@public.example" }""");
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<CommitIdentity> result = await sut.GetAuthenticatedIdentityAsync(CancellationToken.None);

        // Assert
        result.ValueOrThrow().Email.ShouldBe("jane@public.example");
    }

    [Fact]
    public async Task WhenTheUserHasNoEmail_UsesThePrivateCommitAddressOfTheHost()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, """{ "id": 31, "username": "jdoe", "name": null }""");
        GitLabIssueProvider sut = BuildSut(handler, "https://gitlab.example.com/api/v4");

        // Act
        Result<CommitIdentity> result = await sut.GetAuthenticatedIdentityAsync(CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldBe(new CommitIdentity("jdoe", "31-jdoe@users.noreply.gitlab.example.com"));
    }

    [Fact]
    public async Task WhenTheResponseLacksTheIdOrUsername_FailsPermanent()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, """{ "name": "Jane Doe" }""");
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<CommitIdentity> result = await sut.GetAuthenticatedIdentityAsync(CancellationToken.None);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, RunPublishingErrors.TransientCode)]
    [InlineData(HttpStatusCode.TooManyRequests, RunPublishingErrors.TransientCode)]
    [InlineData(HttpStatusCode.Unauthorized, RunPublishingErrors.PermanentCode)]
    public async Task WhenTheProviderFails_ClassifiesTheFailure(HttpStatusCode statusCode, string expectedCode)
    {
        // Arrange
        FakeHandler handler = new(statusCode, "{}");
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<CommitIdentity> result = await sut.GetAuthenticatedIdentityAsync(CancellationToken.None);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(expectedCode);
    }
}
