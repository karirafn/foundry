using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
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
        GitHubAccount account = GitHubAccount.Create("My GitHub", token, BaseUrlFactory.Create("https://github.com"));
        _dbContext.Set<Account>().Add(account);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return account.Id.Value;
    }

    private async Task<Guid> SeedGitLabAccountAsync(string? token = "glpat_test_token")
    {
        GitLabAccount account = GitLabAccount.Create("My GitLab", token, BaseUrlFactory.Create("https://gitlab.com"));
        _dbContext.Set<Account>().Add(account);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return account.Id.Value;
    }

    [Fact]
    public async Task WhenAccountIsGitHub_UsesGitHubClient()
    {
        // Arrange
        Guid accountId = await SeedGitHubAccountAsync();
        IReadOnlyList<AvailableRepository> githubRepos =
        [
            new AvailableRepository("owner/github-repo", IsPrivate: false),
        ];
        FakeHandler gitHubFake = new(HttpStatusCode.OK, BuildGitHubRepoJson(["owner/github-repo"]));
        FakeHandler gitLabFake = new(HttpStatusCode.OK, "[]");
        using HttpClient gitHubHttpClient = new(gitHubFake);
        using HttpClient gitLabHttpClient = new(gitLabFake);
        GetAvailableRepositories.Handler sut = new(
            _dbContext,
            new GitHubHttpClient(gitHubHttpClient),
            new GitLabHttpClient(gitLabHttpClient));

        // Act
        Result<IReadOnlyList<AvailableRepository>> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.ShouldBeOfType<Result<IReadOnlyList<AvailableRepository>>.Success>()
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
        Result<IReadOnlyList<AvailableRepository>> result = await sut.HandleAsync(
            new GetAvailableRepositories.Query(accountId),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.ShouldBeOfType<Result<IReadOnlyList<AvailableRepository>>.Success>()
            .Value.ShouldContain(r => r.Slug == "owner/gitlab-repo");
        gitHubFake.LastRequest.ShouldBeNull();
    }

    private static string BuildGitHubRepoJson(IReadOnlyList<string> fullNames) =>
        "[" + string.Join(",", fullNames.Select(n => $@"{{""full_name"":""{n}"",""private"":false}}")) + "]";

    private static string BuildGitLabRepoJson(IReadOnlyList<string> paths) =>
        "[" + string.Join(",", paths.Select(p => $@"{{""path_with_namespace"":""{p}"",""visibility"":""public""}}")) + "]";
}
