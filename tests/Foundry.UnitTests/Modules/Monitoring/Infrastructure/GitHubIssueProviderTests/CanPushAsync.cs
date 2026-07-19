using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubIssueProviderTests;

public sealed class CanPushAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubIssueProvider BuildSut(FakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(httpClient);
        return new GitHubIssueProvider(gitHubHttpClient, ValidToken, ValidBaseUrl);
    }

    [Fact]
    public async Task WhenClientReturnsTrue_ReturnsTrue()
    {
        // Arrange
        string json = """{ "full_name": "owner/repo", "permissions": { "push": true } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubIssueProvider sut = BuildSut(handler);

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
        string json = """{ "full_name": "owner/repo", "permissions": { "push": false } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubIssueProvider sut = BuildSut(handler);

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
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await sut.CanPushAsync(ValidSlug, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
