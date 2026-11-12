using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.BlockerLinks;

public sealed class BlockerLinkWriterTests : IAsyncDisposable
{
    private const int IssueNumber = 42;
    private const int BlockerNumber = 15;

    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly RecordingBlockerLinkProvider _provider = new();
    private readonly BlockerLinkWriter _sut;

    public BlockerLinkWriterTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbContext = new FoundryDbContext(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();

        _sut = new BlockerLinkWriter(
            _dbContext,
            new SingleIssueProviderFactory(_provider),
            new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance),
            new PostedCommentRegistry(_dbContext),
            new FakeTimeProvider(Now));
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task AddBlockerAsync_WhenTheProviderLinks_SucceedsAfterOneProviderCall()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();

        // Act
        Result result = await _sut.AddBlockerAsync(
            repositoryId, IssueNumber, BlockerNumber, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsSuccess.ShouldBeTrue(),
            () => _provider.Calls.ShouldBe(["link owner/repo #42 blocked by #15"]));
    }

    [Theory]
    [InlineData(BlockerLinkErrors.TransientCode)]
    [InlineData(BlockerLinkErrors.PermanentCode)]
    public async Task AddBlockerAsync_WhenTheProviderFails_PassesItsErrorThrough(string code)
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();
        _provider.SetLinkResult(BlockerNumber, Result.Fail(new Error(code, "provider said no")));

        // Act
        Result result = await _sut.AddBlockerAsync(
            repositoryId, IssueNumber, BlockerNumber, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.ShouldBe(new Error(code, "provider said no"));
    }

    [Fact]
    public async Task AddBlockerAsync_WhenTheRepositoryIsUnknown_FailsPermanentlyWithoutCallingTheProvider()
    {
        // Act
        Result result = await _sut.AddBlockerAsync(
            MonitoredRepositoryId.New(), IssueNumber, BlockerNumber, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(BlockerLinkErrors.PermanentCode),
            () => _provider.Calls.ShouldBeEmpty());
    }

    [Fact]
    public async Task AddBlockerAsync_WhenTheProviderCannotBeReached_FailsTransiently()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();
        _provider.ThrowOnCall = new HttpRequestException("connection refused");

        // Act
        Result result = await _sut.AddBlockerAsync(
            repositoryId, IssueNumber, BlockerNumber, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(BlockerLinkErrors.TransientCode);
    }

    [Fact]
    public async Task AddBlockerAsync_WhenTheProviderTimesOut_FailsTransiently()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();
        _provider.ThrowOnCall = new TaskCanceledException("timed out");

        // Act
        Result result = await _sut.AddBlockerAsync(
            repositoryId, IssueNumber, BlockerNumber, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(BlockerLinkErrors.TransientCode);
    }

    [Fact]
    public async Task AddBlockerAsync_WhenTheCallerCancels_Propagates()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();
        using CancellationTokenSource cancellation = new();
        _provider.OnCall = cancellation.Cancel;
        _provider.ThrowOnCall = new TaskCanceledException("cancelled");

        // Act
        Task<Result> act = _sut.AddBlockerAsync(repositoryId, IssueNumber, BlockerNumber, cancellation.Token);

        // Assert
        await Should.ThrowAsync<TaskCanceledException>(act);
    }

    [Fact]
    public async Task PostIssueCommentAsync_WhenPosted_PostsOnTheIssueThreadAndSavesTheCommentInTheRegistry()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();

        // Act
        Result result = await _sut.PostIssueCommentAsync(
            repositoryId, IssueNumber, "Blocked", TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        List<PostedComment> posted = await _dbContext.Set<PostedComment>()
            .ToListAsync(TestContext.Current.CancellationToken);
        result.ShouldSatisfyAllConditions(
            () => result.IsSuccess.ShouldBeTrue(),
            () => _provider.Calls.ShouldBe([$"comment owner/repo {new CommentThread.Issue(IssueNumber)} Blocked"]),
            () => posted.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                comment => comment.MonitoredRepositoryId.ShouldBe(repositoryId),
                comment => comment.CommentId.ShouldBe(ProviderCommentId.Create(555).ValueOrThrow()),
                comment => comment.PostedAt.ShouldBe(Now)));
    }

    [Theory]
    [InlineData(CommentPostErrors.TransientCode)]
    [InlineData(CommentPostErrors.PermanentCode)]
    public async Task PostIssueCommentAsync_WhenThePostFails_KeepsTheCodeAndRecordsNothing(string code)
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();
        _provider.Comment = Result<ProviderCommentId>.Fail(new Error(code, "post failed"));

        // Act
        Result result = await _sut.PostIssueCommentAsync(
            repositoryId, IssueNumber, "Blocked", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe(code),
            error => error.Message.ShouldBe("post failed"),
            _ => _dbContext.Set<PostedComment>().ShouldBeEmpty());
    }

    [Fact]
    public async Task PostIssueCommentAsync_WhenTheProviderCannotBeReached_FailsTransientlyAndRecordsNothing()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();
        _provider.ThrowOnCall = new HttpRequestException("connection refused");

        // Act
        Result result = await _sut.PostIssueCommentAsync(
            repositoryId, IssueNumber, "Blocked", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(CommentPostErrors.TransientCode),
            () => _dbContext.Set<PostedComment>().ShouldBeEmpty());
    }

    [Fact]
    public async Task PostIssueCommentAsync_WhenTheRepositoryIsUnknown_FailsPermanentlyWithoutCallingTheProvider()
    {
        // Act
        Result result = await _sut.PostIssueCommentAsync(
            MonitoredRepositoryId.New(), IssueNumber, "Blocked", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(CommentPostErrors.PermanentCode),
            () => _provider.Calls.ShouldBeEmpty());
    }

    private async Task<MonitoredRepositoryId> SeedRepoAsync()
    {
        GitHubCredential credential = GitHubCredential.Create(
            "my-org", "ghp_test_token", BaseUrl.Create("https://github.com").ValueOrThrow());
        credential.SetNamespaces([Namespace.Create("owner").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);

        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(), "github.com", null).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repo.Id;
    }
}
