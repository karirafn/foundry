using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;
using Foundry.Modules.Monitoring.Features.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabIssueProviderTests;

public sealed class GetBranchProtectionAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabIssueProvider BuildSut(SequentialFakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitLabHttpClient gitLabHttpClient = new(httpClient, NullLogger<GitLabHttpClient>.Instance);
        return new GitLabIssueProvider(gitLabHttpClient, ValidToken, ValidBaseUrl);
    }

    [Fact]
    public async Task WhenBranchIsFullyProtected_ReturnsAllRejectionsTrue()
    {
        // Arrange
        string projectJson = """{ "default_branch": "main" }""";
        string protectionJson = """
            {
              "name": "main",
              "allow_force_push": false,
              "push_access_levels": [{ "access_level": 0 }]
            }
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, projectJson),
            (HttpStatusCode.OK, protectionJson),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

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
    public async Task WhenBranchHasNoProtection_ReturnsAllRejectionsFalse()
    {
        // Arrange
        string projectJson = """{ "default_branch": "develop" }""";
        string protectionJson = HttpStatusCode.NotFound.ToString();
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, projectJson),
            (HttpStatusCode.NotFound, protectionJson),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

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
    public async Task WhenGetDefaultBranchFails_ReturnsFailure()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<BranchProtection> result = await sut.GetBranchProtectionAsync(
            ValidSlug,
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenProtectionApiFails_ReturnsFailure()
    {
        // Arrange
        string projectJson = """{ "default_branch": "main" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, projectJson),
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<BranchProtection> result = await sut.GetBranchProtectionAsync(
            ValidSlug,
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
