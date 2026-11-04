using System.Data.Common;

using Foundry.Shared.Infrastructure.DataProtection;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore.Diagnostics;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.DataProtection.DataProtectedColumnReEncryptorTests;

public sealed class Atomicity : IAsyncDisposable
{
    private readonly LegacyEncryptedDatabase _database = new();

    async ValueTask IAsyncDisposable.DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task WhenALaterUpdateFails_EarlierRewritesAreRolledBackAndTheExceptionPropagates()
    {
        // Arrange
        await _database.SeedAsync(_database.Legacy, TestContext.Current.CancellationToken);
        Dictionary<string, string?> before = await _database.ReadRawAsync(TestContext.Current.CancellationToken);
        FailOnSecondUpdateInterceptor interceptor = new();
        await using FoundryDbContext dbContext = _database.CreateDbContext(_database.Current, interceptor);
        DataProtectedColumnReEncryptor reEncryptor = new(
            _database.Current,
            _database.Legacy,
            new CapturingLogger<DataProtectedColumnReEncryptor>());

        // Act
        Exception? failure = await Record.ExceptionAsync(
            () => reEncryptor.ReEncryptAsync(dbContext, TestContext.Current.CancellationToken));

        // Assert
        failure.ShouldBeOfType<InvalidOperationException>();
        interceptor.UpdateCount.ShouldBe(2);
        Dictionary<string, string?> after = await _database.ReadRawAsync(TestContext.Current.CancellationToken);
        after.ShouldBe(before);
    }

    private sealed class FailOnSecondUpdateInterceptor : DbCommandInterceptor
    {
        private const string UpdatePrefix = "UPDATE";
        private const int FailingUpdateNumber = 2;

        public int UpdateCount { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith(UpdatePrefix, StringComparison.Ordinal)
                && ++UpdateCount == FailingUpdateNumber)
            {
                throw new InvalidOperationException("Simulated failure on the second UPDATE.");
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
