using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.PackageSourceConfigurationTests;

public sealed class CredentialAtRest : IAsyncDisposable
{
    private const string Token = "registered-token-value";
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public CredentialAtRest()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using FoundryDbContext context = NewContext();
        context.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync() => await _connection.DisposeAsync();

    private FoundryDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);

    private async Task SeedAsync()
    {
        await using FoundryDbContext seed = NewContext();
        PackageSource source = PackageSource.Create(new Uri("https://feed.example/v3/index.json"));
        source.SetCredential("ci-reader", Token, Now);
        seed.Add(source);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheStoredTokenIsNotThePlainToken()
    {
        // Arrange
        await SeedAsync();

        // Act
        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT credential_token FROM package_sources";
        string stored = (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;

        // Assert
        stored.ShouldNotContain(Token);
    }

    [Fact]
    public async Task TheCredentialReadsBackAsSaved()
    {
        // Arrange
        await SeedAsync();

        // Act
        await using FoundryDbContext context = NewContext();
        PackageSource source = await context.Set<PackageSource>().SingleAsync(TestContext.Current.CancellationToken);

        // Assert
        source.Credential.ShouldNotBeNull().ShouldSatisfyAllConditions(
            c => c.Username.ShouldBe("ci-reader"),
            c => c.Token.ShouldBe(new ProviderToken.Present(Token)),
            c => c.UpdatedAt.ShouldBe(Now));
    }

    [Fact]
    public async Task WhenTheTokenCannotBeDecrypted_ReadsAsUnreadable()
    {
        // Arrange
        await SeedAsync();
        await using (SqliteCommand corrupt = _connection.CreateCommand())
        {
            corrupt.CommandText = "UPDATE package_sources SET credential_token = 'not-a-protected-value'";
            await corrupt.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await using FoundryDbContext context = NewContext();
        PackageSource source = await context.Set<PackageSource>().SingleAsync(TestContext.Current.CancellationToken);

        // Assert
        source.Credential.ShouldNotBeNull().Token.ShouldBeOfType<ProviderToken.Unreadable>();
    }
}
