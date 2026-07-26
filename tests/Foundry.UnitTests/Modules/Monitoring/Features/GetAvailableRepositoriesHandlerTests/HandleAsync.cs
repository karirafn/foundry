using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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

    private async Task<Guid> SeedGitHubAccountAsync(string? token = "ghp_test_token")
    {
        GitHubCredential credential = GitHubCredential.Create("My GitHub", token, BaseUrl.Create("https://github.com").ValueOrThrow());
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return credential.Id.Value;
    }

    private async Task<Guid> SeedGitLabAccountAsync(string? token = "glpat_test_token")
    {
        GitLabCredential credential = GitLabCredential.Create("My GitLab", token, BaseUrl.Create("https://gitlab.com").ValueOrThrow());
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return credential.Id.Value;
    }

    [Fact]
    public async Task WhenAccountIsGitHub_UsesGitHubClient()
    {
        // Arrange
        Guid accountId = await SeedGitHubAccountAsync();
        FakeHandler gitHubFake = new(HttpStatusCode.OK, BuildGitHubRepoJson(["owner/github-repo"]));
        FakeHandler gitLabFake = new(HttpStatusCode.OK, "[]");
        using HttpClient gitHubHttpClient = new(gitHubFake);
        using HttpClient gitLabHttpClient = new(gitLabFake);
        GetAvailableRepositories.Handler sut = new(
            _dbContext,
            new GitHubHttpClient(gitHubHttpClient),
            new GitLabHttpClient(gitLabHttpClient));

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Success>()
            .Value.ShouldContain(r => r.Slug == "owner/github-repo");
        gitLabFake.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task WhenAccountIsGitLab_UsesGitLabClient()
    {
        // Arrange
        Guid accountId = await SeedGitLabAccountAsync();
        FakeHandler gitHubFake = new(HttpStatusCode.OK, "[]");
        FakeHandler gitLabFake = new(HttpStatusCode.OK, BuildGitLabRepoJson(["owner/gitlab-repo"]));
        using HttpClient gitHubHttpClient = new(gitHubFake);
        using HttpClient gitLabHttpClient = new(gitLabFake);
        GetAvailableRepositories.Handler sut = new(
            _dbContext,
            new GitHubHttpClient(gitHubHttpClient),
            new GitLabHttpClient(gitLabHttpClient));

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Success>()
            .Value.ShouldContain(r => r.Slug == "owner/gitlab-repo");
        gitHubFake.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task WhenGitHubRepoHasCanPush_PreservesCanPushInResult()
    {
        // Arrange
        Guid accountId = await SeedGitHubAccountAsync();
        string json = """
            [
              {"full_name":"owner/writable","private":false,"permissions":{"push":true}},
              {"full_name":"owner/readonly","private":false,"permissions":{"push":false}}
            ]
            """;
        FakeHandler gitHubFake = new(HttpStatusCode.OK, json);
        FakeHandler gitLabFake = new(HttpStatusCode.OK, "[]");
        using HttpClient gitHubHttpClient = new(gitHubFake);
        using HttpClient gitLabHttpClient = new(gitLabFake);
        GetAvailableRepositories.Handler sut = new(
            _dbContext,
            new GitHubHttpClient(gitHubHttpClient),
            new GitLabHttpClient(gitLabHttpClient));

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        IReadOnlyList<ProviderRepository> repos =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Success>().Value;
        repos.ShouldSatisfyAllConditions(
            () => repos.ShouldContain(r => r.Slug == "owner/writable" && r.CanPush),
            () => repos.ShouldContain(r => r.Slug == "owner/readonly" && !r.CanPush));
    }

    [Fact]
    public async Task WhenGitLabRepoHasCanPush_PreservesCanPushInResult()
    {
        // Arrange
        Guid accountId = await SeedGitLabAccountAsync();
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
        using HttpClient gitHubHttpClient = new(gitHubFake);
        using HttpClient gitLabHttpClient = new(gitLabFake);
        GetAvailableRepositories.Handler sut = new(
            _dbContext,
            new GitHubHttpClient(gitHubHttpClient),
            new GitLabHttpClient(gitLabHttpClient));

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        IReadOnlyList<ProviderRepository> repos =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Success>().Value;
        repos.ShouldContain(r => r.Slug == "owner/gitlab-writable" && r.CanPush);
    }

    private static string BuildGitHubRepoJson(IReadOnlyList<string> fullNames) =>
        "[" + string.Join(",", fullNames.Select(n =>
            $@"{{""full_name"":""{n}"",""private"":false,""permissions"":{{""push"":true}}}}")) + "]";

    private static string BuildGitLabRepoJson(IReadOnlyList<string> paths) =>
        "[" + string.Join(",", paths.Select(p =>
            $@"{{""path_with_namespace"":""{p}"",""visibility"":""public"",""permissions"":{{""project_access"":{{""access_level"":40}},""group_access"":null}}}}")) + "]";
}
