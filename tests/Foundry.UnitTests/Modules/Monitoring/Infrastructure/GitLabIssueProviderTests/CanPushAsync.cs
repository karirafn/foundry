using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
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

public sealed class CanPushAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabIssueProvider BuildSut(FakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitLabHttpClient gitLabHttpClient = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);
        return new GitLabIssueProvider(
            gitLabHttpClient, new ActionableFeedbackPolicy(TimeProvider.System), ValidToken, ValidBaseUrl);
    }

    [Fact]
    public async Task WhenClientReturnsTrue_ReturnsTrue()
    {
        // Arrange
        string json = """
            {
              "id": 1,
              "default_branch": "main",
              "permissions": { "project_access": { "access_level": 40 }, "group_access": null }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await sut.CanPushAsync(ValidSlug, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<bool>.Success success = result.ShouldBeOfType<Result<bool>.Success>();
        success.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenClientReturnsFalse_ReturnsFalse()
    {
        // Arrange
        string json = """
            {
              "id": 1,
              "default_branch": "main",
              "permissions": { "project_access": { "access_level": 20 }, "group_access": null }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await sut.CanPushAsync(ValidSlug, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<bool>.Success success = result.ShouldBeOfType<Result<bool>.Success>();
        success.Value.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenClientReturnsFailure_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await sut.CanPushAsync(ValidSlug, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
