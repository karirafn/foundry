using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.IssueReactions;
using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.IssueReactions.TriageBlockersFoundHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const int IssueNumber = 42;
    private const int FirstBlocker = 15;
    private const int SecondBlocker = 16;
    private const string ProviderRefusal = "Validation failed: cross-repository links are not allowed";

    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly RecordingBlockerLinkProvider _provider = new();
    private readonly CapturingIntegrationEventDispatcher _dispatcher = new();
    private readonly CapturingLogger<TriageBlockersFoundHandler> _logger = new();
    private readonly TriageBlockersFoundHandler _sut;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbContext = new FoundryDbContext(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();

        BlockerLinkWriter writer = new(
            _dbContext,
            new SingleIssueProviderFactory(_provider),
            new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance),
            new PostedCommentRegistry(_dbContext),
            new FakeTimeProvider(Now));
        _sut = new TriageBlockersFoundHandler(writer, _dispatcher, new FakeTimeProvider(Now), _logger);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenEveryLinkSucceeds_LinksEachBlockerInOrderThenPostsOneComment()
    {
        // Arrange
        TriageBlockersFound @event = await SeedEventAsync(
            new TriageBlocker(FirstBlocker, "needs the parser"),
            new TriageBlocker(SecondBlocker, "needs the schema"));

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _provider.Calls.ShouldBe(
            [
                "link owner/repo #42 blocked by #15",
                "link owner/repo #42 blocked by #16",
                $"comment owner/repo Issue {{ IssueNumber = 42 }} {TriageBlockerComment.Compose(@event.Blockers)}",
            ]);
    }

    [Fact]
    public async Task WhenALinkFailsTransiently_ThrowsSoTheRelayRetriesWithoutWritingFurther()
    {
        // Arrange
        TriageBlockersFound @event = await SeedEventAsync(
            new TriageBlocker(FirstBlocker, "needs the parser"),
            new TriageBlocker(SecondBlocker, "needs the schema"));
        _provider.SetLinkResult(FirstBlocker, Result.Fail(BlockerLinkErrors.Transient("503 from provider")));

        // Act
        Func<Task> act = () => _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        await Should.ThrowAsync<InvalidOperationException>(act);
        _provider.Calls.ShouldBe(["link owner/repo #42 blocked by #15"]);
    }

    [Fact]
    public async Task WhenALinkFailsPermanently_ReportsItAndWritesNothingFurther()
    {
        // Arrange
        TriageBlockersFound @event = await SeedEventAsync(
            new TriageBlocker(FirstBlocker, "needs the parser"),
            new TriageBlocker(SecondBlocker, "needs the schema"));
        _provider.SetLinkResult(FirstBlocker, Result.Fail(BlockerLinkErrors.Permanent(ProviderRefusal)));

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBe(
            [new BlockerLinkFailed(@event.MonitoredRepositoryId, IssueNumber, @event.WorkerRunId.Value, FirstBlocker, ProviderRefusal, Now)]);
        _provider.Calls.ShouldBe(["link owner/repo #42 blocked by #15"]);
    }

    [Fact]
    public async Task WhenTheCommentFailsTransiently_Throws()
    {
        // Arrange
        TriageBlockersFound @event = await SeedEventAsync(new TriageBlocker(FirstBlocker, "needs the parser"));
        _provider.Comment = Result<ProviderCommentId>.Fail(CommentPostErrors.Transient("503 from provider"));

        // Act
        Func<Task> act = () => _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        await Should.ThrowAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task WhenTheCommentFailsPermanently_LogsAWarningWithoutThrowing()
    {
        // Arrange
        TriageBlockersFound @event = await SeedEventAsync(new TriageBlocker(FirstBlocker, "needs the parser"));
        _provider.Comment = Result<ProviderCommentId>.Fail(CommentPostErrors.Permanent(ProviderRefusal));

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains(ProviderRefusal));
    }

    [Fact]
    public async Task WhenAPermanentRefusalIsLongAndMultiLine_ReportsItFlattenedAndCapped()
    {
        // Arrange
        TriageBlockersFound @event = await SeedEventAsync(new TriageBlocker(FirstBlocker, "needs the parser"));
        string refusal = $"line one\nline two {new string('x', 600)}";
        _provider.SetLinkResult(FirstBlocker, Result.Fail(BlockerLinkErrors.Permanent(refusal)));

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        string reported = _dispatcher.DispatchedEvents.ShouldHaveSingleItem().ShouldBeOfType<BlockerLinkFailed>().ProviderMessage;
        reported.ShouldSatisfyAllConditions(
            () => reported.ShouldStartWith("line one line two x"),
            () => reported.ShouldNotContain('\n'),
            () => reported.Length.ShouldBeLessThanOrEqualTo(500));
    }

    private async Task<TriageBlockersFound> SeedEventAsync(params TriageBlocker[] blockers)
    {
        GitHubCredential credential = GitHubCredential.Create(
            "my-org", "ghp_test_token", BaseUrl.Create("https://github.com").ValueOrThrow());
        credential.SetNamespaces([Namespace.Create("owner").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);

        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(), "github.com", null).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new TriageBlockersFound(Guid.NewGuid(), repo.Id, IssueNumber, WorkerRunId.New(), blockers);
    }
}
