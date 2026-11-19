using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.GetAccountsHandlerTests;

public sealed class HandleAsyncDeniedRepositories : IAsyncDisposable
{
    private const string GitHubHost = "github.com";
    private const string GitLabHost = "gitlab.example.com";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public HandleAsyncDeniedRepositories()
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

    private GetAccounts.Handler BuildHandler() =>
        new(_dbContext, NullLogger<GetAccounts.Handler>.Instance, TimeProvider.System);

    private async Task<Credential> AddGitHubAccountAsync(string name, params string[] namespaces)
    {
        GitHubCredential credential = GitHubCredential.Create(
            AccountName.Create(name).ValueOrThrow(),
            new IssuedToken("token-value", new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://github.com").ValueOrThrow());
        return await AddAccountAsync(credential, namespaces);
    }

    private async Task<Credential> AddGitLabAccountAsync(string name, params string[] namespaces)
    {
        GitLabCredential credential = GitLabCredential.Create(
            AccountName.Create(name).ValueOrThrow(),
            new IssuedToken("token-value", new TokenExpiry.NotChecked()),
            BaseUrl.Create($"https://{GitLabHost}").ValueOrThrow());
        return await AddAccountAsync(credential, namespaces);
    }

    private async Task<Credential> AddAccountAsync(Credential credential, string[] namespaces)
    {
        credential.SetNamespaces(namespaces.Select(ns => Namespace.Create(ns).ValueOrThrow()));
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return credential;
    }

    private async Task<MonitoredRepository> AddRepositoryAsync(
        string slug,
        string host,
        WriteProbeVerdict verdict,
        int position = 0)
    {
        MonitoredRepository repository = MonitoredRepository
            .Create(RepositorySlug.Create(slug).ValueOrThrow(), host, null, position: position)
            .ValueOrThrow();
        repository.SetWriteProbeVerdict(verdict);
        _dbContext.Set<MonitoredRepository>().Add(repository);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return repository;
    }

    private static WriteProbeVerdict.Denied DeniedFor(params string[] keys) =>
        new(DateTimeOffset.UnixEpoch) { MissingPermissions = keys };

    private async Task<IReadOnlyList<CredentialSummary>> GetAccountsAsync()
    {
        _dbContext.ChangeTracker.Clear();
        Result<IReadOnlyList<CredentialSummary>> result =
            await BuildHandler().HandleAsync(new GetAccounts.Query(), TestContext.Current.CancellationToken);
        return result.ShouldBeOfType<Result<IReadOnlyList<CredentialSummary>>.Success>().Value;
    }

    [Fact]
    public async Task WhenNoRepositoryIsDenied_ReturnsEmptyDeniedRepositories()
    {
        // Arrange
        await AddGitHubAccountAsync("my-org", "my-org");
        await AddRepositoryAsync("my-org/granted", GitHubHost, new WriteProbeVerdict.Granted());
        await AddRepositoryAsync("my-org/unknown", GitHubHost, new WriteProbeVerdict.Unknown(), position: 1);

        // Act
        IReadOnlyList<CredentialSummary> summaries = await GetAccountsAsync();

        // Assert
        summaries.ShouldHaveSingleItem().DeniedRepositories.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenRepositoriesAreDenied_ListsThemWithMissingLabelsInListOrder()
    {
        // Arrange
        await AddGitHubAccountAsync("my-org", "my-org");
        MonitoredRepository first = await AddRepositoryAsync(
            "my-org/api", GitHubHost, DeniedFor("repository_hooks", "administration"), position: 0);
        MonitoredRepository second = await AddRepositoryAsync(
            "my-org/web", GitHubHost, DeniedFor("administration"), position: 1);

        // Act
        IReadOnlyList<CredentialSummary> summaries = await GetAccountsAsync();

        // Assert
        summaries.ShouldHaveSingleItem().DeniedRepositories.ShouldBe([
            new DeniedRepositorySummary(
                first.Id.Value,
                "my-org/api",
                ["Administration (read and write)", "Webhooks (read and write)"]),
            new DeniedRepositorySummary(second.Id.Value, "my-org/web", ["Administration (read and write)"]),
        ], new DeniedRepositorySummaryComparer());
    }

    [Fact]
    public async Task WhenClassicTokenLacksAdminRole_ReportsAdminRoleLabel()
    {
        // Arrange
        await AddGitHubAccountAsync("my-org", "my-org");
        await AddRepositoryAsync("my-org/api", GitHubHost, DeniedFor("admin_role"));

        // Act
        IReadOnlyList<CredentialSummary> summaries = await GetAccountsAsync();

        // Assert
        summaries.ShouldHaveSingleItem().DeniedRepositories.ShouldHaveSingleItem()
            .MissingPermissions.ShouldBe(["Admin role on the repository"]);
    }

    [Fact]
    public async Task WhenGitLabRoleIsBelowMaintainer_ReportsMaintainerRole()
    {
        // Arrange
        await AddGitLabAccountAsync("my-group", "my-group");
        await AddRepositoryAsync("my-group/api", GitLabHost, DeniedFor("maintainer_role"));

        // Act
        IReadOnlyList<CredentialSummary> summaries = await GetAccountsAsync();

        // Assert
        summaries.ShouldHaveSingleItem().DeniedRepositories.ShouldHaveSingleItem()
            .MissingPermissions.ShouldBe(["Maintainer role"]);
    }

    [Fact]
    public async Task WhenAccountsClaimDifferentNamespaces_AttributesEachRepositoryToItsMostSpecificAccount()
    {
        // Arrange
        Credential broad = await AddGitHubAccountAsync("broad", "org");
        Credential narrow = await AddGitHubAccountAsync("narrow", "org/team");
        await AddRepositoryAsync("org/app", GitHubHost, DeniedFor("administration"));
        await AddRepositoryAsync("org/team/app", GitHubHost, DeniedFor("repository_hooks"), position: 1);

        // Act
        IReadOnlyList<CredentialSummary> summaries = await GetAccountsAsync();

        // Assert
        summaries.Single(s => s.Id == broad.Id.Value).DeniedRepositories.ShouldHaveSingleItem().Slug.ShouldBe("org/app");
        summaries.Single(s => s.Id == narrow.Id.Value).DeniedRepositories.ShouldHaveSingleItem().Slug.ShouldBe("org/team/app");
    }

    [Fact]
    public async Task WhenDeniedRepositoryIsOnAnotherHost_DoesNotListIt()
    {
        // Arrange
        await AddGitHubAccountAsync("my-org", "my-org");
        await AddRepositoryAsync("my-org/api", GitLabHost, DeniedFor("maintainer_role"));

        // Act
        IReadOnlyList<CredentialSummary> summaries = await GetAccountsAsync();

        // Assert
        summaries.ShouldHaveSingleItem().DeniedRepositories.ShouldBeEmpty();
    }

    private sealed class DeniedRepositorySummaryComparer : IEqualityComparer<DeniedRepositorySummary>
    {
        public bool Equals(DeniedRepositorySummary? x, DeniedRepositorySummary? y) =>
            x is not null
            && y is not null
            && x.Id == y.Id
            && x.Slug == y.Slug
            && x.MissingPermissions.SequenceEqual(y.MissingPermissions);

        public int GetHashCode(DeniedRepositorySummary obj) => HashCode.Combine(obj.Id, obj.Slug);
    }
}
