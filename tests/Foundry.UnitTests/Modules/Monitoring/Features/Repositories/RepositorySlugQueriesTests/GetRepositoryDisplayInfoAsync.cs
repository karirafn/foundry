using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Repositories.RepositorySlugQueriesTests;

public sealed class GetRepositoryDisplayInfoAsync : IAsyncDisposable
{
    private const string SelfHostedBaseUrl = "https://git.example.com";
    private const string SelfHostedHost = "git.example.com";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IRepositorySlugQueries _sut;

    public GetRepositoryDisplayInfoAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new RepositorySlugQueries(_dbContext);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static RepositorySlug ValidSlug(string slug) =>
        RepositorySlug.Create(slug).ValueOrThrow();

    private static BaseUrl ValidBaseUrl(string url) =>
        BaseUrl.Create(url).ValueOrThrow();

    private async Task<MonitoredRepository> AddRepositoryAsync(string slug, string host, int position = 0)
    {
        MonitoredRepository repo = MonitoredRepository.Create(ValidSlug(slug), host, null, position: position).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return repo;
    }

    private async Task AddCredentialAsync(Credential credential, string ns)
    {
        credential.SetNamespaces([Namespace.Create(ns).ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenNoIdsProvided_ReturnsEmptyDictionary()
    {
        // Arrange
        IReadOnlySet<MonitoredRepositoryId> ids = new HashSet<MonitoredRepositoryId>();

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, RepositoryDisplayInfo> result =
            await _sut.GetRepositoryDisplayInfoAsync(ids, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIdsDoNotMatchAnyRepository_ReturnsEmptyDictionary()
    {
        // Arrange
        IReadOnlySet<MonitoredRepositoryId> ids = new HashSet<MonitoredRepositoryId>
        {
            MonitoredRepositoryId.New(),
            MonitoredRepositoryId.New(),
        };

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, RepositoryDisplayInfo> result =
            await _sut.GetRepositoryDisplayInfoAsync(ids, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenRepositoriesOnDifferentHosts_ReturnsSlugAndProviderOfEachCoveringCredential()
    {
        // Arrange
        await AddCredentialAsync(GitHubCredential.Create("gh", "TOKEN", ValidBaseUrl("https://github.com")), "owner");
        await AddCredentialAsync(GitLabCredential.Create("gl", "TOKEN", ValidBaseUrl("https://gitlab.com")), "group");
        MonitoredRepository githubRepo = await AddRepositoryAsync("owner/repo", "github.com", position: 0);
        MonitoredRepository gitlabRepo = await AddRepositoryAsync("group/project", "gitlab.com", position: 1);

        IReadOnlySet<MonitoredRepositoryId> ids = new HashSet<MonitoredRepositoryId> { githubRepo.Id, gitlabRepo.Id };

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, RepositoryDisplayInfo> result =
            await _sut.GetRepositoryDisplayInfoAsync(ids, TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldSatisfyAllConditions(
            () => result[githubRepo.Id].ShouldBe(new RepositoryDisplayInfo("owner/repo", "github", 0)),
            () => result[gitlabRepo.Id].ShouldBe(new RepositoryDisplayInfo("group/project", "gitlab", 1)));
    }

    [Fact]
    public async Task WhenNoCoveringCredential_ReturnsSlugWithNullProviderType()
    {
        // Arrange
        await AddCredentialAsync(GitHubCredential.Create("gh", "TOKEN", ValidBaseUrl("https://github.com")), "someone-else");
        MonitoredRepository repo = await AddRepositoryAsync("uncovered/repo", "github.com");

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, RepositoryDisplayInfo> result =
            await _sut.GetRepositoryDisplayInfoAsync(
                new HashSet<MonitoredRepositoryId> { repo.Id },
                TestContext.Current.CancellationToken);

        // Assert
        result[repo.Id].ShouldBe(new RepositoryDisplayInfo("uncovered/repo", null, 0));
    }

    [Fact]
    public async Task WhenCoveringCredentialIsOnAnotherHost_ReturnsNullProviderType()
    {
        // Arrange — the namespace matches, but the credential belongs to a different host
        await AddCredentialAsync(GitLabCredential.Create("gl", "TOKEN", ValidBaseUrl("https://gitlab.com")), "owner");
        MonitoredRepository repo = await AddRepositoryAsync("owner/repo", "github.com");

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, RepositoryDisplayInfo> result =
            await _sut.GetRepositoryDisplayInfoAsync(
                new HashSet<MonitoredRepositoryId> { repo.Id },
                TestContext.Current.CancellationToken);

        // Assert
        result[repo.Id].ProviderType.ShouldBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenNestedNamespacesOnOneHost_ReturnsProviderOfMoreSpecificCredential_MatchingCredentialResolver(
        bool addSpecificFirst)
    {
        // Arrange — a GitHub credential owns "group", a GitLab credential owns "group/sub" on the same host
        GitHubCredential broad = GitHubCredential.Create("broad", "TOKEN", ValidBaseUrl(SelfHostedBaseUrl));
        GitLabCredential specific = GitLabCredential.Create("specific", "TOKEN", ValidBaseUrl(SelfHostedBaseUrl));

        if (addSpecificFirst)
        {
            await AddCredentialAsync(specific, "group/sub");
            await AddCredentialAsync(broad, "group");
        }
        else
        {
            await AddCredentialAsync(broad, "group");
            await AddCredentialAsync(specific, "group/sub");
        }

        MonitoredRepository nested = await AddRepositoryAsync("group/sub/project", SelfHostedHost, position: 0);
        MonitoredRepository shallow = await AddRepositoryAsync("group/other", SelfHostedHost, position: 1);
        _dbContext.ChangeTracker.Clear();

        CredentialResolver resolver = new(_dbContext, NullLogger<CredentialResolver>.Instance);
        Credential? resolvedForNested = await resolver.ResolveAsync(
            SelfHostedHost,
            nested.Slug,
            TestContext.Current.CancellationToken);

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, RepositoryDisplayInfo> result =
            await _sut.GetRepositoryDisplayInfoAsync(
                new HashSet<MonitoredRepositoryId> { nested.Id, shallow.Id },
                TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => resolvedForNested.ShouldBeOfType<GitLabCredential>(),
            () => result[nested.Id].ProviderType.ShouldBe("gitlab"),
            () => result[shallow.Id].ProviderType.ShouldBe("github"));
    }
}
