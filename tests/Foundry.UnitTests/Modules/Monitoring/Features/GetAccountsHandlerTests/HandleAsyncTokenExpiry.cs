using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.GetAccountsHandlerTests;

public sealed class HandleAsyncTokenExpiry : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public HandleAsyncTokenExpiry()
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

    private async Task<TokenExpirySummary> StoreAndReadAsync(TokenExpiry expiry)
    {
        GitLabCredential credential = GitLabCredential.Create(
            AccountName.Create("alice").ValueOrThrow(),
            new IssuedToken("glpat_token", expiry),
            BaseUrl.Create("https://gitlab.com").ValueOrThrow());
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        GetAccounts.Handler handler = new(_dbContext, NullLogger<GetAccounts.Handler>.Instance, new FakeTimeProvider(Now));
        Result<IReadOnlyList<CredentialSummary>> result =
            await handler.HandleAsync(new GetAccounts.Query(), TestContext.Current.CancellationToken);

        return result.ValueOrThrow().ShouldHaveSingleItem().TokenExpiry;
    }

    [Fact]
    public async Task WhenTheStoredExpiryIsWithinTheWarningWindow_ReturnsExpiringSoonWithTheDate()
    {
        // Act
        TokenExpirySummary summary = await StoreAndReadAsync(new TokenExpiry.On(Now.AddDays(10)));

        // Assert
        summary.ShouldBe(new TokenExpirySummary("expiringSoon", Now.AddDays(10)));
    }

    [Fact]
    public async Task WhenTheStoredExpiryIsBeyondTheWarningWindow_ReturnsCurrentWithTheDate()
    {
        // Act
        TokenExpirySummary summary = await StoreAndReadAsync(new TokenExpiry.On(Now.AddDays(30)));

        // Assert
        summary.ShouldBe(new TokenExpirySummary("current", Now.AddDays(30)));
    }

    [Fact]
    public async Task WhenTheStoredExpiryHasPassed_ReturnsExpiredWithTheDate()
    {
        // Act
        TokenExpirySummary summary = await StoreAndReadAsync(new TokenExpiry.On(Now.AddDays(-1)));

        // Assert
        summary.ShouldBe(new TokenExpirySummary("expired", Now.AddDays(-1)));
    }

    [Fact]
    public async Task WhenTheTokenNeverExpires_ReturnsNeverWithNoDate()
    {
        // Act
        TokenExpirySummary summary = await StoreAndReadAsync(new TokenExpiry.Never());

        // Assert
        summary.ShouldBe(new TokenExpirySummary("never", null));
    }

    [Fact]
    public async Task WhenTheProviderReportedNoExpiry_ReturnsUnavailableWithNoDate()
    {
        // Act
        TokenExpirySummary summary = await StoreAndReadAsync(new TokenExpiry.Unavailable());

        // Assert
        summary.ShouldBe(new TokenExpirySummary("unavailable", null));
    }

    [Fact]
    public async Task WhenTheExpiryWasNotChecked_ReturnsNotCheckedWithNoDate()
    {
        // Act
        TokenExpirySummary summary = await StoreAndReadAsync(new TokenExpiry.NotChecked());

        // Assert
        summary.ShouldBe(new TokenExpirySummary("notChecked", null));
    }
}
