using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.PackageSources;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PackageSources.PackageSourceVerdictsTests;

public sealed class VerdictForAsync : IAsyncDisposable
{
    private const string SourceUrl = "https://gitlab.com/api/v4/groups/7/-/packages/nuget/index.json";

    private readonly SqliteConnection _connection;

    public VerdictForAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using FoundryDbContext context = NewContext();
        context.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync() => await _connection.DisposeAsync();

    private FoundryDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);

    [Fact]
    public async Task WhenTheSourceWasProbedWithTheCredential_ReturnsTheStoredVerdict()
    {
        // Arrange
        ProbeCredential account = new ProbeCredential.Account(CredentialId.New());
        await using (FoundryDbContext seed = NewContext())
        {
            PackageSource source = PackageSource.Create(new Uri(SourceUrl));
            source.RecordProbe(
                account,
                1,
                new PackageSourceVerdict.Unreadable(403, "search returned 403"),
                new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero));
            seed.Add(source);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext context = NewContext();

        // Act
        PackageSourceVerdict verdict = await new PackageSourceVerdicts(context)
            .VerdictForAsync(new Uri(SourceUrl), account, TestContext.Current.CancellationToken);

        // Assert
        verdict.ShouldBe(new PackageSourceVerdict.Unreadable(403, "search returned 403"));
    }

    [Fact]
    public async Task WhenTheSourceIsNotStored_ReturnsUnchecked()
    {
        // Arrange
        await using FoundryDbContext context = NewContext();

        // Act
        PackageSourceVerdict verdict = await new PackageSourceVerdicts(context)
            .VerdictForAsync(new Uri(SourceUrl), new ProbeCredential.Anonymous(), TestContext.Current.CancellationToken);

        // Assert
        verdict.ShouldBeOfType<PackageSourceVerdict.Unchecked>();
    }
}
