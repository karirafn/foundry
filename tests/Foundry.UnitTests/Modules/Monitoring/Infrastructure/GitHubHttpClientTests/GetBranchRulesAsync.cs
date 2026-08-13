using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class GetBranchRulesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    [Fact]
    public async Task WhenBranchHasNonFastForwardRule_RejectsForceAndDeletion()
    {
        // Arrange
        string json = """
            [
              { "type": "non_fast_forward" }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<BranchRules> result = await sut.GetBranchRulesAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<BranchRules>.Success success = result.ShouldBeOfType<Result<BranchRules>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.RejectForcePushes.ShouldBeTrue(),
            () => success.Value.RejectDirectPushes.ShouldBeFalse(),
            () => success.Value.RejectDeletion.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenBranchHasDeletionRule_RejectsDeletion()
    {
        // Arrange
        string json = """
            [
              { "type": "deletion" }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<BranchRules> result = await sut.GetBranchRulesAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<BranchRules>.Success success = result.ShouldBeOfType<Result<BranchRules>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.RejectDeletion.ShouldBeTrue(),
            () => success.Value.RejectForcePushes.ShouldBeFalse(),
            () => success.Value.RejectDirectPushes.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenBranchHasPullRequestRule_RejectsDirectPushes()
    {
        // Arrange
        string json = """
            [
              { "type": "pull_request" }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<BranchRules> result = await sut.GetBranchRulesAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<BranchRules>.Success success = result.ShouldBeOfType<Result<BranchRules>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.RejectDirectPushes.ShouldBeTrue(),
            () => success.Value.RejectForcePushes.ShouldBeFalse(),
            () => success.Value.RejectDeletion.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenBranchHasAllProtectionRules_RejectsAll()
    {
        // Arrange
        string json = """
            [
              { "type": "non_fast_forward" },
              { "type": "deletion" },
              { "type": "pull_request" }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<BranchRules> result = await sut.GetBranchRulesAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<BranchRules>.Success success = result.ShouldBeOfType<Result<BranchRules>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.RejectDirectPushes.ShouldBeTrue(),
            () => success.Value.RejectForcePushes.ShouldBeTrue(),
            () => success.Value.RejectDeletion.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenBranchHasUnknownRules_IgnoresUnknownTypes()
    {
        // Arrange
        string json = """
            [
              { "type": "unknown_rule_type" },
              { "type": "another_future_rule" }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<BranchRules> result = await sut.GetBranchRulesAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<BranchRules>.Success success = result.ShouldBeOfType<Result<BranchRules>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.RejectDirectPushes.ShouldBeFalse(),
            () => success.Value.RejectForcePushes.ShouldBeFalse(),
            () => success.Value.RejectDeletion.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenApiReturns404_ReturnsNoProtection()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<BranchRules> result = await sut.GetBranchRulesAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<BranchRules>.Success success = result.ShouldBeOfType<Result<BranchRules>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.RejectDirectPushes.ShouldBeFalse(),
            () => success.Value.RejectForcePushes.ShouldBeFalse(),
            () => success.Value.RejectDeletion.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenApiReturnsNonSuccessNonNotFound_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<BranchRules> result = await sut.GetBranchRulesAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<BranchRules>.Failure failure = result.ShouldBeOfType<Result<BranchRules>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectEndpointUrl()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        await sut.GetBranchRulesAsync(ValidBaseUrl, ValidSlug, "main", "ghp_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/repos/owner/repo/rules/branches/main");
    }

    [Fact]
    public async Task WhenBaseUrlHasInvalidScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        Uri invalidBaseUrl = new("ftp://api.github.com");

        // Act
        Result<BranchRules> result = await sut.GetBranchRulesAsync(
            invalidBaseUrl,
            ValidSlug,
            "main",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<BranchRules>.Failure failure = result.ShouldBeOfType<Result<BranchRules>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }
}
