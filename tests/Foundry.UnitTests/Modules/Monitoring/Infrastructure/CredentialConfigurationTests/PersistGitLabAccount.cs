using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.CredentialConfigurationTests;

public sealed class PersistGitLabAccount : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistGitLabAccount()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        IDataProtectionProvider dataProtectionProvider = DataProtectionProvider.Create("Foundry.Test");

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options, dataProtectionProvider);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenGitLabCredentialPersisted_CanBeReloadedAsGitLabCredential()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://gitlab.com").ValueOrThrow();
        GitLabCredential credential = GitLabCredential.Create("my-org", "glpat_mytoken", baseUrl);

        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Credential? result = await _dbContext
            .Set<Credential>()
            .FindAsync([credential.Id], TestContext.Current.CancellationToken);

        // Assert
        GitLabCredential gitLab = result.ShouldBeOfType<GitLabCredential>();
        gitLab.ShouldSatisfyAllConditions(
            () => gitLab.Id.ShouldBe(credential.Id),
            () => gitLab.Name.ShouldBe("my-org"),
            () => gitLab.Token.ShouldBe("glpat_mytoken"),
            () => gitLab.BaseUrl.Value.ShouldBe(new Uri("https://gitlab.com")),
            () => gitLab.Host.ShouldBe("gitlab.com"));
    }

    [Fact]
    public async Task WhenGitLabCredentialPersistedWithNullToken_CanBeReloadedWithNullToken()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://gitlab.com").ValueOrThrow();
        GitLabCredential credential = GitLabCredential.Create("my-org", null, baseUrl);

        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Credential? result = await _dbContext
            .Set<Credential>()
            .FindAsync([credential.Id], TestContext.Current.CancellationToken);

        // Assert
        GitLabCredential gitLab = result.ShouldBeOfType<GitLabCredential>();
        gitLab.Token.ShouldBeNull();
    }
}
