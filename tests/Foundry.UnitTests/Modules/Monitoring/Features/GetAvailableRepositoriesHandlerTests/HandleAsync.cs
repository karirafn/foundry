using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.GetAvailableRepositoriesHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<(Guid AccountId, GitHubCredential Credential)> SeedGitHubAccountAsync(
        string? token = "ghp_test_token",
        string[]? namespaces = null)
    {
        GitHubCredential credential = GitHubCredential.Create(
            "My GitHub",
            token,
            BaseUrl.Create("https://github.com").ValueOrThrow());

        if (namespaces is not null)
        {
            credential.SetNamespaces(namespaces.Select(v => Namespace.Create(v).ValueOrThrow()));
        }

        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return (credential.Id.Value, credential);
    }

    private async Task<(Guid AccountId, GitLabCredential Credential)> SeedGitLabAccountAsync(
        string? token = "glpat_test_token",
        string[]? namespaces = null)
    {
        GitLabCredential credential = GitLabCredential.Create(
            "My GitLab",
            token,
            BaseUrl.Create("https://gitlab.com").ValueOrThrow());

        if (namespaces is not null)
        {
            credential.SetNamespaces(namespaces.Select(v => Namespace.Create(v).ValueOrThrow()));
        }

        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return (credential.Id.Value, credential);
    }

    private async Task SeedMonitoredRepositoryAsync(string slug, string host)
    {
        RepositorySlug repositorySlug = RepositorySlug.Create(slug).ValueOrThrow();
        MonitoredRepository repo = MonitoredRepository.Create(repositorySlug, host, null);
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private GetAvailableRepositories.Handler BuildHandler(
        FakeHandler gitHubFake,
        FakeHandler gitLabFake)
    {
        // HttpClient lifetime is managed by the caller via FakeHandler — do not dispose here.
        HttpClient gitHubHttpClient = new(gitHubFake);
        HttpClient gitLabHttpClient = new(gitLabFake);
        return new GetAvailableRepositories.Handler(
            _dbContext,
            new GitHubHttpClient(gitHubHttpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System),
            new GitLabHttpClient(gitLabHttpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System));
    }

    [Fact]
    public async Task WhenAccountIsGitHub_UsesGitHubClient()
    {
        // Arrange
        (Guid accountId, _) = await SeedGitHubAccountAsync(namespaces: ["owner"]);
        FakeHandler gitHubFake = new(HttpStatusCode.OK, BuildGitHubRepoJson(["owner/github-repo"]));
        FakeHandler gitLabFake = new(HttpStatusCode.OK, "[]");
        GetAvailableRepositories.Handler sut = BuildHandler(gitHubFake, gitLabFake);

        // Act
        Result<AvailableRepositoriesResponse> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.ShouldBeOfType<Result<AvailableRepositoriesResponse>.Success>()
            .Value.Repositories.ShouldContain(r => r.Slug == "owner/github-repo");
        gitLabFake.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task WhenAccountIsGitLab_UsesGitLabClient()
    {
        // Arrange
        (Guid accountId, _) = await SeedGitLabAccountAsync(namespaces: ["owner"]);
        FakeHandler gitHubFake = new(HttpStatusCode.OK, "[]");
        FakeHandler gitLabFake = new(HttpStatusCode.OK, BuildGitLabRepoJson(["owner/gitlab-repo"]));
        GetAvailableRepositories.Handler sut = BuildHandler(gitHubFake, gitLabFake);

        // Act
        Result<AvailableRepositoriesResponse> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.ShouldBeOfType<Result<AvailableRepositoriesResponse>.Success>()
            .Value.Repositories.ShouldContain(r => r.Slug == "owner/gitlab-repo");
        gitHubFake.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task WhenGitHubRepoHasCanPush_PreservesCanPushInResult()
    {
        // Arrange
        (Guid accountId, _) = await SeedGitHubAccountAsync(namespaces: ["owner"]);
        string json = """
            [
              {"full_name":"owner/writable","private":false,"permissions":{"push":true}},
              {"full_name":"owner/readonly","private":false,"permissions":{"push":false}}
            ]
            """;
        FakeHandler gitHubFake = new(HttpStatusCode.OK, json);
        FakeHandler gitLabFake = new(HttpStatusCode.OK, "[]");
        GetAvailableRepositories.Handler sut = BuildHandler(gitHubFake, gitLabFake);

        // Act
        Result<AvailableRepositoriesResponse> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        IReadOnlyList<AvailableRepository> repos =
            result.ShouldBeOfType<Result<AvailableRepositoriesResponse>.Success>().Value.Repositories;
        repos.ShouldSatisfyAllConditions(
            () => repos.ShouldContain(r => r.Slug == "owner/writable" && r.CanPush),
            () => repos.ShouldContain(r => r.Slug == "owner/readonly" && !r.CanPush));
    }

    [Fact]
    public async Task WhenGitLabRepoHasCanPush_PreservesCanPushInResult()
    {
        // Arrange
        (Guid accountId, _) = await SeedGitLabAccountAsync(namespaces: ["owner"]);
        string json = """
            [
              {
                "path_with_namespace": "owner/gitlab-writable",
                "visibility": "public",
                "permissions": {
                  "project_access": { "access_level": 30 },
                  "group_access": null
                }
              }
            ]
            """;
        FakeHandler gitHubFake = new(HttpStatusCode.OK, "[]");
        FakeHandler gitLabFake = new(HttpStatusCode.OK, json);
        GetAvailableRepositories.Handler sut = BuildHandler(gitHubFake, gitLabFake);

        // Act
        Result<AvailableRepositoriesResponse> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        IReadOnlyList<AvailableRepository> repos =
            result.ShouldBeOfType<Result<AvailableRepositoriesResponse>.Success>().Value.Repositories;
        repos.ShouldContain(r => r.Slug == "owner/gitlab-writable" && r.CanPush);
    }

    [Fact]
    public async Task WhenRepoIsUnderClaimedNamespace_IsIncluded()
    {
        // Arrange
        (Guid accountId, _) = await SeedGitHubAccountAsync(namespaces: ["my-org"]);
        FakeHandler gitHubFake = new(HttpStatusCode.OK, BuildGitHubRepoJson(["my-org/repo-a", "other-org/repo-b"]));
        FakeHandler gitLabFake = new(HttpStatusCode.OK, "[]");
        GetAvailableRepositories.Handler sut = BuildHandler(gitHubFake, gitLabFake);

        // Act
        Result<AvailableRepositoriesResponse> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        IReadOnlyList<AvailableRepository> repos =
            result.ShouldBeOfType<Result<AvailableRepositoriesResponse>.Success>().Value.Repositories;
        repos.ShouldContain(r => r.Slug == "my-org/repo-a");
    }

    [Fact]
    public async Task WhenRepoIsNotUnderAnyClaimedNamespace_IsExcluded()
    {
        // Arrange
        (Guid accountId, _) = await SeedGitHubAccountAsync(namespaces: ["my-org"]);
        FakeHandler gitHubFake = new(HttpStatusCode.OK, BuildGitHubRepoJson(["my-org/repo-a", "other-org/repo-b"]));
        FakeHandler gitLabFake = new(HttpStatusCode.OK, "[]");
        GetAvailableRepositories.Handler sut = BuildHandler(gitHubFake, gitLabFake);

        // Act
        Result<AvailableRepositoriesResponse> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        IReadOnlyList<AvailableRepository> repos =
            result.ShouldBeOfType<Result<AvailableRepositoriesResponse>.Success>().Value.Repositories;
        repos.ShouldNotContain(r => r.Slug == "other-org/repo-b");
    }

    [Fact]
    public async Task WhenRepoIsUnderNamespaceClaimedByDifferentAccount_IsExcluded()
    {
        // Arrange — this account claims "my-org", not "other-org"
        (Guid accountId, _) = await SeedGitHubAccountAsync(namespaces: ["my-org"]);
        // Seed a second account that claims "other-org" (but we don't use its id here)
        await SeedGitHubAccountAsync(token: "ghp_other_token", namespaces: ["other-org"]);
        FakeHandler gitHubFake = new(HttpStatusCode.OK, BuildGitHubRepoJson(["my-org/repo-a", "other-org/repo-b"]));
        FakeHandler gitLabFake = new(HttpStatusCode.OK, "[]");
        GetAvailableRepositories.Handler sut = BuildHandler(gitHubFake, gitLabFake);

        // Act
        Result<AvailableRepositoriesResponse> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        IReadOnlyList<AvailableRepository> repos =
            result.ShouldBeOfType<Result<AvailableRepositoriesResponse>.Success>().Value.Repositories;
        repos.ShouldNotContain(r => r.Slug == "other-org/repo-b");
    }

    [Fact]
    public async Task WhenNestedGroupNamespaceIsClaimed_ChildPathIsIncluded()
    {
        // Arrange — claim on "group" covers "group/subgroup/project"
        (Guid accountId, _) = await SeedGitLabAccountAsync(namespaces: ["group"]);
        FakeHandler gitHubFake = new(HttpStatusCode.OK, "[]");
        FakeHandler gitLabFake = new(HttpStatusCode.OK, BuildGitLabRepoJson(["group/subgroup/project"]));
        GetAvailableRepositories.Handler sut = BuildHandler(gitHubFake, gitLabFake);

        // Act
        Result<AvailableRepositoriesResponse> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        IReadOnlyList<AvailableRepository> repos =
            result.ShouldBeOfType<Result<AvailableRepositoriesResponse>.Success>().Value.Repositories;
        repos.ShouldContain(r => r.Slug == "group/subgroup/project");
    }

    [Fact]
    public async Task WhenMonitoredRepositoryExistsForSameHostAndSlug_IsMonitoredIsTrue()
    {
        // Arrange
        (Guid accountId, _) = await SeedGitHubAccountAsync(namespaces: ["owner"]);
        await SeedMonitoredRepositoryAsync("owner/monitored-repo", "github.com");
        FakeHandler gitHubFake = new(HttpStatusCode.OK, BuildGitHubRepoJson(["owner/monitored-repo"]));
        FakeHandler gitLabFake = new(HttpStatusCode.OK, "[]");
        GetAvailableRepositories.Handler sut = BuildHandler(gitHubFake, gitLabFake);

        // Act
        Result<AvailableRepositoriesResponse> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        IReadOnlyList<AvailableRepository> repos =
            result.ShouldBeOfType<Result<AvailableRepositoriesResponse>.Success>().Value.Repositories;
        repos.ShouldContain(r => r.Slug == "owner/monitored-repo" && r.IsMonitored);
    }

    [Fact]
    public async Task WhenMonitoredRepositoryDoesNotExistForSlug_IsMonitoredIsFalse()
    {
        // Arrange
        (Guid accountId, _) = await SeedGitHubAccountAsync(namespaces: ["owner"]);
        FakeHandler gitHubFake = new(HttpStatusCode.OK, BuildGitHubRepoJson(["owner/unmonitored-repo"]));
        FakeHandler gitLabFake = new(HttpStatusCode.OK, "[]");
        GetAvailableRepositories.Handler sut = BuildHandler(gitHubFake, gitLabFake);

        // Act
        Result<AvailableRepositoriesResponse> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        IReadOnlyList<AvailableRepository> repos =
            result.ShouldBeOfType<Result<AvailableRepositoriesResponse>.Success>().Value.Repositories;
        repos.ShouldContain(r => r.Slug == "owner/unmonitored-repo" && !r.IsMonitored);
    }

    [Fact]
    public async Task WhenSameSlugIsMonitoredOnDifferentHost_IsMonitoredIsFalse()
    {
        // Arrange
        (Guid accountId, _) = await SeedGitHubAccountAsync(namespaces: ["owner"]);
        // Seed the slug on gitlab.com, not github.com
        await SeedMonitoredRepositoryAsync("owner/repo", "gitlab.com");
        FakeHandler gitHubFake = new(HttpStatusCode.OK, BuildGitHubRepoJson(["owner/repo"]));
        FakeHandler gitLabFake = new(HttpStatusCode.OK, "[]");
        GetAvailableRepositories.Handler sut = BuildHandler(gitHubFake, gitLabFake);

        // Act
        Result<AvailableRepositoriesResponse> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        IReadOnlyList<AvailableRepository> repos =
            result.ShouldBeOfType<Result<AvailableRepositoriesResponse>.Success>().Value.Repositories;
        repos.ShouldContain(r => r.Slug == "owner/repo" && !r.IsMonitored);
    }

    [Fact]
    public async Task WhenCredentialHasZeroNamespaces_HasClaimsIsFalseAndReposIsEmpty()
    {
        // Arrange
        (Guid accountId, _) = await SeedGitHubAccountAsync(namespaces: []);
        FakeHandler gitHubFake = new(HttpStatusCode.OK, BuildGitHubRepoJson(["owner/repo"]));
        FakeHandler gitLabFake = new(HttpStatusCode.OK, "[]");
        GetAvailableRepositories.Handler sut = BuildHandler(gitHubFake, gitLabFake);

        // Act
        Result<AvailableRepositoriesResponse> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        AvailableRepositoriesResponse response =
            result.ShouldBeOfType<Result<AvailableRepositoriesResponse>.Success>().Value;
        response.ShouldSatisfyAllConditions(
            () => response.HasClaims.ShouldBeFalse(),
            () => response.Repositories.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenProviderReturnsError_HandlerReturnsFailure()
    {
        // Arrange
        (Guid accountId, _) = await SeedGitHubAccountAsync(namespaces: ["owner"]);
        FakeHandler gitHubFake = new(HttpStatusCode.ServiceUnavailable, "error");
        FakeHandler gitLabFake = new(HttpStatusCode.OK, "[]");
        GetAvailableRepositories.Handler sut = BuildHandler(gitHubFake, gitLabFake);

        // Act
        Result<AvailableRepositoriesResponse> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeFalse();
    }

    private static string BuildGitHubRepoJson(IReadOnlyList<string> fullNames) =>
        "[" + string.Join(",", fullNames.Select(n =>
            $@"{{""full_name"":""{n}"",""private"":false,""permissions"":{{""push"":true}}}}")) + "]";

    private static string BuildGitLabRepoJson(IReadOnlyList<string> paths) =>
        "[" + string.Join(",", paths.Select(p =>
            $@"{{""path_with_namespace"":""{p}"",""visibility"":""public"",""permissions"":{{""project_access"":{{""access_level"":40}},""group_access"":null}}}}")) + "]";
}
