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
using Foundry.Modules.Monitoring.Features.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubIssueProviderTests;

public sealed class GetBranchProtectionAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubIssueProvider BuildSut(SequentialFakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        return new GitHubIssueProvider(gitHubHttpClient, ValidToken, ValidBaseUrl);
    }

    [Fact]
    public async Task WhenRepositoryHasFullProtection_ReturnsAllRejectionsTrue()
    {
        // Arrange
        string repoJson = """{ "default_branch": "main" }""";
        string rulesJson = """
            [
              { "type": "non_fast_forward" },
              { "type": "deletion" },
              { "type": "pull_request" }
            ]
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, repoJson),
            (HttpStatusCode.OK, rulesJson),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<BranchProtection> result = await sut.GetBranchProtectionAsync(
            ValidSlug,
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<BranchProtection>.Success success = result.ShouldBeOfType<Result<BranchProtection>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.DefaultBranch.ShouldBe("main"),
            () => success.Value.RejectDirectPushes.ShouldBeTrue(),
            () => success.Value.RejectForcePushes.ShouldBeTrue(),
            () => success.Value.RejectDeletion.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenRepositoryHasNoRules_ReturnsAllRejectionsFalse()
    {
        // Arrange
        string repoJson = """{ "default_branch": "develop" }""";
        string rulesJson = "[]";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, repoJson),
            (HttpStatusCode.OK, rulesJson),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<BranchProtection> result = await sut.GetBranchProtectionAsync(
            ValidSlug,
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<BranchProtection>.Success success = result.ShouldBeOfType<Result<BranchProtection>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.DefaultBranch.ShouldBe("develop"),
            () => success.Value.RejectDirectPushes.ShouldBeFalse(),
            () => success.Value.RejectForcePushes.ShouldBeFalse(),
            () => success.Value.RejectDeletion.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenRulesApiReturns404_ReturnsSuccessWithNoProtection()
    {
        // Arrange
        string repoJson = """{ "default_branch": "main" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, repoJson),
            (HttpStatusCode.NotFound, string.Empty),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<BranchProtection> result = await sut.GetBranchProtectionAsync(
            ValidSlug,
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<BranchProtection>.Success success = result.ShouldBeOfType<Result<BranchProtection>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.DefaultBranch.ShouldBe("main"),
            () => success.Value.RejectDirectPushes.ShouldBeFalse(),
            () => success.Value.RejectForcePushes.ShouldBeFalse(),
            () => success.Value.RejectDeletion.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenRepoApiReturnsFails_ReturnsFailure()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<BranchProtection> result = await sut.GetBranchProtectionAsync(
            ValidSlug,
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenRulesApiReturnsFails_ReturnsFailure()
    {
        // Arrange
        string repoJson = """{ "default_branch": "main" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, repoJson),
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<BranchProtection> result = await sut.GetBranchProtectionAsync(
            ValidSlug,
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
