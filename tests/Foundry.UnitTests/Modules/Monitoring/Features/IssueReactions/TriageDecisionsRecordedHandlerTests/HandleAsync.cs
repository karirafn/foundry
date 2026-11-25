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
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.IssueReactions.TriageDecisionsRecordedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const int IssueNumber = 42;
    private const long PostedCommentId = 555;
    private const string ProviderRefusal = "Validation failed: issue is locked";

    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly RecordingBlockerLinkProvider _provider = new();
    private readonly CapturingLogger<TriageDecisionsRecordedHandler> _logger = new();
    private readonly TriageDecisionsRecordedHandler _sut;

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
        _sut = new TriageDecisionsRecordedHandler(writer, _logger);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenDecisionsAreRecorded_PostsOneCommentListingThemOnTheIssue()
    {
        // Arrange
        TriageDecisionsRecorded @event = await SeedEventAsync(TriageRelease.Queued);

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _provider.Calls.ShouldBe(
            [$"comment owner/repo Issue {{ IssueNumber = 42 }} {TriageDecisionComment.Compose(@event.Decisions, TriageRelease.Queued)}"]);
    }

    [Fact]
    public async Task WhenTheCommentIsPosted_RegistersItsIdSoItIsNeverReadAsInput()
    {
        // Arrange
        TriageDecisionsRecorded @event = await SeedEventAsync(TriageRelease.Queued);

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        PostedComment registered = (await _dbContext.Set<PostedComment>().AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        registered.ShouldSatisfyAllConditions(
            () => registered.MonitoredRepositoryId.ShouldBe(@event.MonitoredRepositoryId),
            () => registered.CommentId.ShouldBe(ProviderCommentId.Create(PostedCommentId).ValueOrThrow()));
    }

    [Fact]
    public async Task WhenTheEventIsRedelivered_PostsNoSecondComment()
    {
        // Arrange
        TriageDecisionsRecorded @event = await SeedEventAsync(TriageRelease.Queued);
        ServiceCollection services = new();
        services.AddSingleton<IIntegrationEventHandler<TriageDecisionsRecorded>>(_sut);
        HandlerDedupIdentityRegistry identities = new();
        identities.Register(typeof(TriageDecisionsRecordedHandler));
        IntegrationEventProcessor processor = new(services.BuildServiceProvider(), _dbContext, identities);
        Guid eventId = Guid.NewGuid();

        // Act
        await processor.ProcessAsync(eventId, @event, TestContext.Current.CancellationToken);
        await processor.ProcessAsync(eventId, @event, TestContext.Current.CancellationToken);

        // Assert
        _provider.Calls.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenTheCommentFailsTransiently_ThrowsSoTheRelayRetries()
    {
        // Arrange
        TriageDecisionsRecorded @event = await SeedEventAsync(TriageRelease.Queued);
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
        TriageDecisionsRecorded @event = await SeedEventAsync(TriageRelease.WaitingOnBlockers);
        _provider.Comment = Result<ProviderCommentId>.Fail(CommentPostErrors.Permanent(ProviderRefusal));

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains(ProviderRefusal));
    }

    private async Task<TriageDecisionsRecorded> SeedEventAsync(TriageRelease release)
    {
        GitHubCredential credential = GitHubCredential.Create(
            AccountName.Create("my-org").ValueOrThrow(),
            new IssuedToken("ghp_" + "test_token", new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://github.com").ValueOrThrow());
        credential.SetNamespaces([Namespace.Create("owner").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);

        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(), "github.com", null).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new TriageDecisionsRecorded(
            Guid.NewGuid(),
            repo.Id,
            IssueNumber,
            [TriageDecisionBuilder.Build(), TriageDecisionBuilder.Build(question: "Which log level?", ground: TriageDecisionGround.ImplementationDetail)],
            release);
    }
}
