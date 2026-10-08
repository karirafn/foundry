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

    private GetAccounts.Handler BuildHandler() => new(_dbContext, NullLogger<GetAccounts.Handler>.Instance);

    [Fact]
    public async Task WhenCredentialHasToken_ReturnsHasTokenTrue()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_token", baseUrl);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        GetAccounts.Handler handler = BuildHandler();

        // Act
        Result<IReadOnlyList<CredentialSummary>> result =
            await handler.HandleAsync(new GetAccounts.Query(), TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<CredentialSummary> summaries = result.ShouldBeOfType<Result<IReadOnlyList<CredentialSummary>>.Success>().Value;
        CredentialSummary summary = summaries.ShouldHaveSingleItem();
        summary.HasToken.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenCredentialHasToken_ReturnsTokenStatusPresent()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_token", baseUrl);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        GetAccounts.Handler handler = BuildHandler();

        // Act
        Result<IReadOnlyList<CredentialSummary>> result =
            await handler.HandleAsync(new GetAccounts.Query(), TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<CredentialSummary> summaries = result.ShouldBeOfType<Result<IReadOnlyList<CredentialSummary>>.Success>().Value;
        CredentialSummary summary = summaries.ShouldHaveSingleItem();
        summary.TokenStatus.ShouldBe("present");
    }

    [Fact]
    public async Task WhenCredentialHasNoToken_ReturnsHasTokenFalse()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("my-org", null, baseUrl);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        GetAccounts.Handler handler = BuildHandler();

        // Act
        Result<IReadOnlyList<CredentialSummary>> result =
            await handler.HandleAsync(new GetAccounts.Query(), TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<CredentialSummary> summaries = result.ShouldBeOfType<Result<IReadOnlyList<CredentialSummary>>.Success>().Value;
        CredentialSummary summary = summaries.ShouldHaveSingleItem();
        summary.HasToken.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenCredentialHasNoToken_ReturnsTokenStatusAbsent()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("my-org", null, baseUrl);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        GetAccounts.Handler handler = BuildHandler();

        // Act
        Result<IReadOnlyList<CredentialSummary>> result =
            await handler.HandleAsync(new GetAccounts.Query(), TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<CredentialSummary> summaries = result.ShouldBeOfType<Result<IReadOnlyList<CredentialSummary>>.Success>().Value;
        CredentialSummary summary = summaries.ShouldHaveSingleItem();
        summary.TokenStatus.ShouldBe("absent");
    }

    [Fact]
    public async Task WhenCredentialHasUnreadableToken_ReturnsHasTokenTrue()
    {
        // Arrange — seed garbage ciphertext directly; the ProviderTokenConverter blocks persisting
        // an Unreadable token (it originated from a failed decryption and has no storable value).
        // A non-base64 string triggers a FormatException on read, producing ProviderToken.Unreadable.
        // HasToken must remain true: the column is non-null, meaning a token is present (just corrupt).
        await SeedCredentialWithGarbageTokenAsync("my-org");

        GetAccounts.Handler handler = BuildHandler();

        // Act
        Result<IReadOnlyList<CredentialSummary>> result =
            await handler.HandleAsync(new GetAccounts.Query(), TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<CredentialSummary> summaries = result.ShouldBeOfType<Result<IReadOnlyList<CredentialSummary>>.Success>().Value;
        CredentialSummary summary = summaries.ShouldHaveSingleItem();
        summary.HasToken.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenCredentialHasUnreadableToken_ReturnsTokenStatusUnreadable()
    {
        // Arrange — garbage ciphertext forces FormatException in ProviderTokenConverter, yielding Unreadable.
        await SeedCredentialWithGarbageTokenAsync("my-org");

        GetAccounts.Handler handler = BuildHandler();

        // Act
        Result<IReadOnlyList<CredentialSummary>> result =
            await handler.HandleAsync(new GetAccounts.Query(), TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<CredentialSummary> summaries = result.ShouldBeOfType<Result<IReadOnlyList<CredentialSummary>>.Success>().Value;
        CredentialSummary summary = summaries.ShouldHaveSingleItem();
        summary.TokenStatus.ShouldBe("unreadable");
    }

    /// <summary>
    /// Inserts a GitHub credential row with a garbage token ciphertext directly via raw SQL.
    /// The ProviderTokenConverter throws on reads and writes of Unreadable tokens, so raw SQL
    /// is the only way to reach this state in a unit test without a real corrupt DB.
    /// </summary>
    private async Task SeedCredentialWithGarbageTokenAsync(string name)
    {
        string id = Guid.NewGuid().ToString();
        await _dbContext.Database.ExecuteSqlAsync(
            $"INSERT INTO accounts (id, name, token, base_url, host, type) VALUES ({id}, {name}, {"not-valid-base64!!!"}, {"https://github.com/"}, {"github.com"}, {"github"})",
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenGitHubCredential_ReturnsGitHubProviderType()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_token", baseUrl);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        GetAccounts.Handler handler = BuildHandler();

        // Act
        Result<IReadOnlyList<CredentialSummary>> result =
            await handler.HandleAsync(new GetAccounts.Query(), TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<CredentialSummary> summaries = result.ShouldBeOfType<Result<IReadOnlyList<CredentialSummary>>.Success>().Value;
        CredentialSummary summary = summaries.ShouldHaveSingleItem();
        summary.ProviderType.ShouldBe("github");
    }

    [Fact]
    public async Task WhenCredentialHasNamespaces_ProjectsNamespaceValues()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_token", baseUrl);
        credential.SetNamespaces([Namespace.Create("org-a").ValueOrThrow(), Namespace.Create("org-b").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        GetAccounts.Handler handler = BuildHandler();

        // Act
        Result<IReadOnlyList<CredentialSummary>> result =
            await handler.HandleAsync(new GetAccounts.Query(), TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<CredentialSummary> summaries = result.ShouldBeOfType<Result<IReadOnlyList<CredentialSummary>>.Success>().Value;
        CredentialSummary summary = summaries.ShouldHaveSingleItem();
        summary.Namespaces.Count.ShouldBe(2);
        summary.Namespaces.ShouldContain("org-a");
        summary.Namespaces.ShouldContain("org-b");
    }
}
