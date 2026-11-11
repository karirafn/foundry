using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

using Registry = Foundry.Modules.Monitoring.Features.Providers.Comments.PostedCommentRegistry;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.Comments.PostedCommentRegistryTests;

public sealed class PostedCommentRegistry : IAsyncDisposable
{
    private static readonly DateTimeOffset PostedAt = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private int _nextPosition = 1;

    public PostedCommentRegistry()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using FoundryDbContext dbContext = NewContext();
        dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private FoundryDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);

    private static ProviderCommentId Id(long value) => ProviderCommentId.Create(value).ValueOrThrow();

    private static ProviderComment Conversation(long id) =>
        new ProviderCommentBuilder().WithId(id).WithOrigin(CommentOrigin.Conversation).Build();

    private MonitoredRepository SeedRepository(string slug)
    {
        using FoundryDbContext dbContext = NewContext();
        MonitoredRepository repository = new MonitoredRepositoryBuilder()
            .WithSlug(RepositorySlug.Create(slug).ValueOrThrow())
            .WithPosition(_nextPosition++)
            .Build();
        dbContext.Set<MonitoredRepository>().Add(repository);
        dbContext.SaveChanges();
        return repository;
    }

    [Fact]
    public async Task WhenCommentIsRecordedAndSaved_LoadIncludesIt()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository("owner/repo");
        await using (FoundryDbContext writeContext = NewContext())
        {
            new Registry(writeContext).Record(repository.Id, Id(500), PostedAt);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext readContext = NewContext();
        Registry sut = new(readContext);

        // Act
        PostedCommentIds ids = await sut.LoadAsync(repository.Id, CancellationToken.None);

        // Assert
        ids.ShouldSatisfyAllConditions(
            () => ids.Contains(Conversation(500)).ShouldBeTrue(),
            () => ids.Contains(Conversation(501)).ShouldBeFalse());
    }

    [Fact]
    public async Task WhenAnotherRepositoryPostedTheId_LoadExcludesIt()
    {
        // Arrange — comment IDs are only unique within one repository's provider
        MonitoredRepository repository = SeedRepository("owner/repo");
        MonitoredRepository other = SeedRepository("owner/other");
        await using (FoundryDbContext writeContext = NewContext())
        {
            new Registry(writeContext).Record(other.Id, Id(500), PostedAt);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext readContext = NewContext();
        Registry sut = new(readContext);

        // Act
        PostedCommentIds ids = await sut.LoadAsync(repository.Id, CancellationToken.None);

        // Assert
        ids.Contains(Conversation(500)).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenSameCommentIsRecordedTwiceBeforeSaving_SavesOneEntry()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository("owner/repo");
        await using FoundryDbContext dbContext = NewContext();
        Registry sut = new(dbContext);

        // Act
        sut.Record(repository.Id, Id(500), PostedAt);
        sut.Record(repository.Id, Id(500), PostedAt);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        (await dbContext.Set<PostedComment>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task WhenRepositoryIsDeleted_ItsEntriesAreDeleted()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository("owner/repo");
        await using (FoundryDbContext writeContext = NewContext())
        {
            new Registry(writeContext).Record(repository.Id, Id(500), PostedAt);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = NewContext();

        // Act
        await dbContext.Set<MonitoredRepository>().Where(r => r.Id == repository.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        // Assert
        (await dbContext.Set<PostedComment>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }
}
