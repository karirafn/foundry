using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain;
using Foundry.Modules.Credentials.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.GetCredentialsTests;

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

    [Fact]
    public async Task WhenAccountExists_ReturnsSummaryWithAccountId()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        _dbContext.Set<ClaudeAccount>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        GetCredentials.Handler sut = new(_dbContext);

        // Act
        Result<ClaudeAccountSummary> result = await sut.HandleAsync(
            new GetCredentials.Query(),
            TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccountSummary summary = result.ShouldBeOfType<Result<ClaudeAccountSummary>.Success>().Value;
        summary.AccountId.ShouldBe(ClaudeAccountId.Default.Value);
    }

    [Fact]
    public async Task WhenNoAccountExists_ReturnsFailure()
    {
        // Arrange
        GetCredentials.Handler sut = new(_dbContext);

        // Act
        Result<ClaudeAccountSummary> result = await sut.HandleAsync(
            new GetCredentials.Query(),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<ClaudeAccountSummary>.Failure>();
    }
}
