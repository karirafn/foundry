using System.Net;
using System.Text;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.NamespaceDerivation.NamespaceDeriverTests;

public sealed class DeriveAsync
{
    private const string GitHubListingJson = """
        [
          { "full_name": "octocat/repo-a", "private": false, "permissions": { "push": true } },
          { "full_name": "octocat/repo-b", "private": true,  "permissions": { "push": true } }
        ]
        """;

    private static GitHubCredential BuildGitHubCredential(string token = "ghp_test")
    {
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        return GitHubCredential.Create("octocat", token, baseUrl);
    }

    private static NamespaceDeriver BuildSut(HttpClient gitHubClient, HttpClient? gitLabClient = null)
    {
        GitHubHttpClient gh = new(gitHubClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);
        GitLabHttpClient gl = new(gitLabClient ?? new HttpClient(new NotCalledHandler()), NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);
        return new NamespaceDeriver(gh, gl, NullLogger<NamespaceDeriver>.Instance);
    }

    [Fact]
    public async Task RawToken_WhenGitHubListingSucceeds_ReturnsDerivedWithNamespaces()
    {
        // Arrange
        Uri apiBaseUrl = new("https://api.github.com/");
        HttpClient httpClient = new(new StaticJsonHandler(HttpStatusCode.OK, GitHubListingJson));
        NamespaceDeriver sut = BuildSut(httpClient);

        // Act
        NamespaceDerivationOutcome outcome = await sut.DeriveAsync(
            apiBaseUrl,
            token: "ghp_test",
            isGitLab: false,
            CancellationToken.None);

        // Assert
        NamespaceDerivationOutcome.Derived derived = outcome.ShouldBeOfType<NamespaceDerivationOutcome.Derived>();
        derived.Namespaces.Count.ShouldBe(1);
        derived.Namespaces.ShouldContain(ns => ns.Value == "octocat");
    }

    private const string GitLabListingJson = """
        [
          { "path_with_namespace": "gitlab-user/project-a", "visibility": "public",
            "permissions": { "project_access": { "access_level": 40 }, "group_access": null } }
        ]
        """;

    [Fact]
    public async Task RawToken_WhenGitLabListingSucceeds_ReturnsDerivedWithNamespaces()
    {
        // Arrange
        Uri apiBaseUrl = new("https://gitlab.com/");
        HttpClient gitLabClient = new(new StaticJsonHandler(HttpStatusCode.OK, GitLabListingJson));
        NamespaceDeriver sut = BuildSut(new HttpClient(new NotCalledHandler()), gitLabClient);

        // Act
        NamespaceDerivationOutcome outcome = await sut.DeriveAsync(
            apiBaseUrl,
            token: "glpat-test",
            isGitLab: true,
            CancellationToken.None);

        // Assert
        NamespaceDerivationOutcome.Derived derived = outcome.ShouldBeOfType<NamespaceDerivationOutcome.Derived>();
        derived.Namespaces.Count.ShouldBe(1);
        derived.Namespaces.ShouldContain(ns => ns.Value == "gitlab-user");
    }

    [Fact]
    public async Task RawToken_WhenListingResultIsFailure_ReturnsUnavailable()
    {
        // Arrange
        Uri apiBaseUrl = new("https://api.github.com/");
        HttpClient httpClient = new(new StaticJsonHandler(HttpStatusCode.Unauthorized, "{}"));
        NamespaceDeriver sut = BuildSut(httpClient);

        // Act
        NamespaceDerivationOutcome outcome = await sut.DeriveAsync(
            apiBaseUrl,
            token: "ghp_bad",
            isGitLab: false,
            CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<NamespaceDerivationOutcome.Unavailable>();
    }

    [Fact]
    public async Task WhenListingSucceeds_ReturnsDerivedWithNamespaces()
    {
        // Arrange
        GitHubCredential credential = BuildGitHubCredential();
        HttpClient httpClient = new(new StaticJsonHandler(HttpStatusCode.OK, GitHubListingJson));
        NamespaceDeriver sut = BuildSut(httpClient);

        // Act
        NamespaceDerivationOutcome outcome = await sut.DeriveAsync(credential, CancellationToken.None);

        // Assert
        NamespaceDerivationOutcome.Derived derived = outcome.ShouldBeOfType<NamespaceDerivationOutcome.Derived>();
        derived.Namespaces.Count.ShouldBe(1);
        derived.Namespaces.ShouldContain(ns => ns.Value == "octocat");
    }

    [Fact]
    public async Task WhenListingResultIsFailure_ReturnsUnavailable()
    {
        // Arrange
        GitHubCredential credential = BuildGitHubCredential();
        HttpClient httpClient = new(new StaticJsonHandler(HttpStatusCode.Unauthorized, "{}"));
        NamespaceDeriver sut = BuildSut(httpClient);

        // Act
        NamespaceDerivationOutcome outcome = await sut.DeriveAsync(credential, CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<NamespaceDerivationOutcome.Unavailable>();
    }

    [Fact]
    public async Task WhenListingThrowsNonCancellation_ReturnsUnavailable()
    {
        // Arrange
        GitHubCredential credential = BuildGitHubCredential();
        HttpClient httpClient = new(new ThrowingHandler(new HttpRequestException("network error")));
        NamespaceDeriver sut = BuildSut(httpClient);

        // Act
        NamespaceDerivationOutcome outcome = await sut.DeriveAsync(credential, CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<NamespaceDerivationOutcome.Unavailable>();
    }

    [Fact]
    public async Task WhenListingThrowsOperationCanceled_PropagatesException()
    {
        // Arrange
        GitHubCredential credential = BuildGitHubCredential();
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();
        HttpClient httpClient = new(new ThrowingHandler(new OperationCanceledException(cts.Token)));
        NamespaceDeriver sut = BuildSut(httpClient);

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => sut.DeriveAsync(credential, cts.Token));
    }

    [Fact]
    public async Task WhenCredentialHasNoToken_ReturnsUnavailable()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("octocat", token: null, baseUrl);
        NamespaceDeriver sut = BuildSut(new HttpClient(new NotCalledHandler()));

        // Act
        NamespaceDerivationOutcome outcome = await sut.DeriveAsync(credential, CancellationToken.None);

        // Assert
        outcome.ShouldBeOfType<NamespaceDerivationOutcome.Unavailable>();
    }

    private sealed class StaticJsonHandler(HttpStatusCode statusCode, string json) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler(Exception exception) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class NotCalledHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HTTP client should not have been called.");
    }
}
