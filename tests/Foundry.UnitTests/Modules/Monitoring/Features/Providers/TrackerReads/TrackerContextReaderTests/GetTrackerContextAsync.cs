using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Providers.TrackerReads;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.TrackerReads.TrackerContextReaderTests;

public sealed class GetTrackerContextAsync : IAsyncDisposable
{
    private const int IssueNumber = 7;
    private const int OpenIssueCap = 100;

    private static readonly IssueContent Content = new("Add retry", "Retry webhook delivery.", []);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly TrackerReadsProvider _provider = new();
    private readonly ITrackerContextQueries _sut;

    public GetTrackerContextAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbContext = new FoundryDbContext(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();

        _sut = new TrackerContextReader(
            _dbContext,
            new SingleIssueProviderFactory(_provider),
            new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance),
            NullLogger<TrackerContextReader>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private ITrackerContextQueries SutWith(ILogger<TrackerContextReader> logger) =>
        new TrackerContextReader(
            _dbContext,
            new SingleIssueProviderFactory(_provider),
            new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance),
            logger);

    private async Task<MonitoredRepositoryId> SeedRepoAsync()
    {
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_test_token", BaseUrl.Create("https://github.com").ValueOrThrow());
        credential.SetNamespaces([Namespace.Create("owner").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);

        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(), "github.com", null).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repo.Id;
    }

    private async Task<TrackerContext> ReadAsync(
        CancellationToken? cancellationToken = null,
        IssueContent? content = null) =>
        await _sut.GetTrackerContextAsync(
            await SeedRepoAsync(),
            IssueNumber,
            content ?? Content,
            cancellationToken ?? TestContext.Current.CancellationToken);

    private static ProviderReferencedIssue ReferencedRead(string title, string body = "Body") =>
        new(title, true, ["bug"], ClosingMergeRequestState.Open, body);

    [Fact]
    public async Task WhenTheIssueLinksAndMentionsOthers_ListsOneReferencedIssuePerSelectedReferenceInOrder()
    {
        // Arrange
        _provider.Links = Result<IReadOnlyList<IssueReference>>.Ok([new IssueReference(5, IssueRelation.BlockedBy)]);
        _provider.ReferencedIssues[5] = Result<ProviderReferencedIssue>.Ok(ReferencedRead("Linked", "Linked body"));
        _provider.ReferencedIssues[9] = Result<ProviderReferencedIssue>.Ok(
            new ProviderReferencedIssue("Mentioned", false, [], ClosingMergeRequestState.Merged, "Mentioned body"));
        IssueContent content = new IssueContentBuilder().WithBody("See #9.").Build();

        // Act
        TrackerContext context = await ReadAsync(content: content);

        // Assert
        context.ReferencedIssues.Count.ShouldBe(2);
        ReferencedIssue linked = context.ReferencedIssues[0];
        ReferencedIssue mentioned = context.ReferencedIssues[1];
        linked.ShouldSatisfyAllConditions(
            () => linked.Number.ShouldBe(5),
            () => linked.Relation.ShouldBe(IssueRelation.BlockedBy),
            () => linked.Title.ShouldBe("Linked"),
            () => linked.IsOpen.ShouldBeTrue(),
            () => linked.Labels.ShouldBe(["bug"]),
            () => linked.ClosingMergeRequest.ShouldBe(ClosingMergeRequestState.Open),
            () => linked.Body.ShouldBe("Linked body"));
        mentioned.ShouldSatisfyAllConditions(
            () => mentioned.Number.ShouldBe(9),
            () => mentioned.Relation.ShouldBe(IssueRelation.Mention),
            () => mentioned.Title.ShouldBe("Mentioned"),
            () => mentioned.IsOpen.ShouldBeFalse(),
            () => mentioned.Labels.ShouldBeEmpty(),
            () => mentioned.ClosingMergeRequest.ShouldBe(ClosingMergeRequestState.Merged),
            () => mentioned.Body.ShouldBe("Mentioned body"));
    }

    [Fact]
    public async Task WhenAReferenceReadFailsOrThrows_LeavesItOutListsItAsUnreadableAndStillReadsTheRest()
    {
        // Arrange
        _provider.Links = Result<IReadOnlyList<IssueReference>>.Ok(
            [
                new IssueReference(5, IssueRelation.BlockedBy),
                new IssueReference(6, IssueRelation.Blocks),
                new IssueReference(8, IssueRelation.RelatesTo),
                new IssueReference(9, IssueRelation.RelatesTo),
            ]);
        _provider.ReferencedIssues[5] = Result<ProviderReferencedIssue>.Fail(new Error("GitHub.Forbidden", "no access"));
        _provider.ReferencedIssues[6] = Result<ProviderReferencedIssue>.Ok(ReferencedRead("Readable"));
        _provider.ThrowingReferencedIssues.Add(8);
        _provider.OpenIssues = Result<IReadOnlyList<OpenIssue>>.Ok([new OpenIssue(30, "Newest")]);

        // Act
        TrackerContext context = await ReadAsync();

        // Assert
        context.ShouldSatisfyAllConditions(
            () => context.ReferencedIssues.Select(issue => issue.Number).ShouldBe([6]),
            () => context.UnreadableIssueNumbers.ShouldBe([5, 8, 9]),
            () => context.OpenIssues.ShouldNotBeNull().Select(openIssue => openIssue.Number).ShouldBe([30]));
    }

    [Theory]
    [InlineData(20, 20)]
    [InlineData(21, 20)]
    public async Task WhenAReferencedIssueHasManyLabels_KeepsTheFirstTwenty(int labelCount, int expectedCount)
    {
        // Arrange
        List<string> labels = [.. Enumerable.Range(1, labelCount).Select(number => $"label-{number}")];
        _provider.ReferencedIssues[9] = Result<ProviderReferencedIssue>.Ok(
            new ProviderReferencedIssue("Mentioned", true, labels, ClosingMergeRequestState.None, "Body"));
        IssueContent content = new IssueContentBuilder().WithBody("See #9.").Build();

        // Act
        TrackerContext context = await ReadAsync(content: content);

        // Assert
        context.ReferencedIssues.Single().Labels.ShouldBe(labels.Take(expectedCount));
    }

    [Theory]
    [InlineData(256, 256)]
    [InlineData(257, 256)]
    public async Task WhenAReferencedIssueHasALongTitleAndLabel_CutsEachToTwoHundredFiftySixCharacters(
        int length,
        int expectedLength)
    {
        // Arrange
        string text = new('a', length);
        _provider.ReferencedIssues[9] = Result<ProviderReferencedIssue>.Ok(
            new ProviderReferencedIssue(text, true, [text], ClosingMergeRequestState.None, "Body"));
        IssueContent content = new IssueContentBuilder().WithBody("See #9.").Build();

        // Act
        TrackerContext context = await ReadAsync(content: content);

        // Assert
        ReferencedIssue issue = context.ReferencedIssues.Single();
        (issue.Title.Length, issue.Labels.Single().Length).ShouldBe((expectedLength, expectedLength));
    }

    [Fact]
    public async Task WhenTheCutFallsInsideASurrogatePair_DropsTheWholePair()
    {
        // Arrange
        string title = new string('a', 255) + "\U0001F600";
        _provider.ReferencedIssues[9] = Result<ProviderReferencedIssue>.Ok(
            new ProviderReferencedIssue(title, true, [], ClosingMergeRequestState.None, "Body"));
        IssueContent content = new IssueContentBuilder().WithBody("See #9.").Build();

        // Act
        TrackerContext context = await ReadAsync(content: content);

        // Assert
        context.ReferencedIssues.Single().Title.ShouldBe(new string('a', 255));
    }

    [Theory]
    [InlineData(256, 256)]
    [InlineData(257, 256)]
    public async Task WhenAnOpenIssueHasALongTitle_CutsItToTwoHundredFiftySixCharacters(int length, int expectedLength)
    {
        // Arrange
        _provider.OpenIssues = Result<IReadOnlyList<OpenIssue>>.Ok([new OpenIssue(30, new string('a', length))]);

        // Act
        TrackerContext context = await ReadAsync();

        // Assert
        context.OpenIssues.ShouldNotBeNull().Single().Title.Length.ShouldBe(expectedLength);
    }

    [Fact]
    public async Task WhenAReferencedIssueIsConfidential_ListsItByNumberOnlyAsUnreadable()
    {
        // Arrange
        IssueContent content = new IssueContentBuilder().WithBody("See #9.").Build();
        _provider.ReferencedIssues[9] = Result<ProviderReferencedIssue>.Fail(TrackerReadErrors.Confidential());

        // Act
        TrackerContext context = await ReadAsync(content: content);

        // Assert
        context.ShouldSatisfyAllConditions(
            () => context.ReferencedIssues.ShouldBeEmpty(),
            () => context.UnreadableIssueNumbers.ShouldBe([9]));
    }

    [Fact]
    public async Task WhenAReferenceIsNotAnIssue_LeavesItOutWithoutListingItAsUnreadable()
    {
        // Arrange
        IssueContent content = new IssueContentBuilder().WithBody("Fixed by #868, blocked on #123456.").Build();
        _provider.ReferencedIssues[868] = Result<ProviderReferencedIssue>.Fail(TrackerReadErrors.NotAnIssue());
        _provider.ReferencedIssues[123456] = Result<ProviderReferencedIssue>.Fail(TrackerReadErrors.NotAnIssue());

        // Act
        TrackerContext context = await ReadAsync(content: content);

        // Assert
        context.ShouldSatisfyAllConditions(
            () => context.ReferencedIssues.ShouldBeEmpty(),
            () => context.UnreadableIssueNumbers.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenAReferenceIsNotFound_LeavesItOutWithoutANoteOrAWarning()
    {
        // Arrange
        CapturingLogger<TrackerContextReader> logger = new();
        ITrackerContextQueries sut = SutWith(logger);
        IssueContent content = new IssueContentBuilder().WithBody("Blocked on #123456.").Build();
        _provider.ReferencedIssues[123456] = Result<ProviderReferencedIssue>.Fail(TrackerReadErrors.NotFound());

        // Act
        TrackerContext context = await sut.GetTrackerContextAsync(
            await SeedRepoAsync(), IssueNumber, content, TestContext.Current.CancellationToken);

        // Assert
        context.ShouldSatisfyAllConditions(
            () => context.ReferencedIssues.ShouldBeEmpty(),
            () => context.UnreadableIssueNumbers.ShouldBeEmpty(),
            () => logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenAReferenceIsDroppedSilently_LogsADebugEntryWithTheRepositoryTheNumbersAndTheErrorCode(
        bool notFound)
    {
        // Arrange
        CapturingLogger<TrackerContextReader> logger = new();
        ITrackerContextQueries sut = SutWith(logger);
        IssueContent content = new IssueContentBuilder().WithBody("Blocked on #123456.").Build();
        Error dropped = notFound ? TrackerReadErrors.NotFound() : TrackerReadErrors.NotAnIssue();
        _provider.ReferencedIssues[123456] = Result<ProviderReferencedIssue>.Fail(dropped);

        // Act
        await sut.GetTrackerContextAsync(
            await SeedRepoAsync(), IssueNumber, content, TestContext.Current.CancellationToken);

        // Assert
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Debug
            && entry.Message.Contains("owner/repo", StringComparison.Ordinal)
            && entry.Message.Contains("#7", StringComparison.Ordinal)
            && entry.Message.Contains("#123456", StringComparison.Ordinal)
            && entry.Message.Contains(dropped.Code, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("GitHub.UnexpectedStatusCode", "GitHub API returned unexpected status code 403.")]
    [InlineData("GitHub.UnexpectedStatusCode", "GitHub API returned unexpected status code 503.")]
    public async Task WhenAReferenceFailsForAnotherReason_ListsItAsUnreadableAndWarnsWithTheErrorAndTheNumbers(
        string code,
        string message)
    {
        // Arrange
        CapturingLogger<TrackerContextReader> logger = new();
        ITrackerContextQueries sut = SutWith(logger);
        IssueContent content = new IssueContentBuilder().WithBody("Blocked on #9.").Build();
        _provider.ReferencedIssues[9] = Result<ProviderReferencedIssue>.Fail(new Error(code, message));

        // Act
        TrackerContext context = await sut.GetTrackerContextAsync(
            await SeedRepoAsync(), IssueNumber, content, TestContext.Current.CancellationToken);

        // Assert
        context.ShouldSatisfyAllConditions(
            () => context.UnreadableIssueNumbers.ShouldBe([9]),
            () => logger.Entries.ShouldContain(entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("owner/repo", StringComparison.Ordinal)
                && entry.Message.Contains("#7", StringComparison.Ordinal)
                && entry.Message.Contains("#9", StringComparison.Ordinal)
                && entry.Message.Contains(code, StringComparison.Ordinal)
                && entry.Message.Contains(message, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task WhenAReferenceIsNotAnIssue_DoesNotWarn()
    {
        // Arrange
        CapturingLogger<TrackerContextReader> logger = new();
        ITrackerContextQueries sut = SutWith(logger);
        IssueContent content = new IssueContentBuilder().WithBody("Fixed by #868.").Build();
        _provider.ReferencedIssues[868] = Result<ProviderReferencedIssue>.Fail(TrackerReadErrors.NotAnIssue());

        // Act
        await sut.GetTrackerContextAsync(
            await SeedRepoAsync(), IssueNumber, content, TestContext.Current.CancellationToken);

        // Assert
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task WhenTheLinksReadFails_WarnsWithTheRepositoryTheIssueAndTheError()
    {
        // Arrange
        CapturingLogger<TrackerContextReader> logger = new();
        ITrackerContextQueries sut = SutWith(logger);
        _provider.Links = Result<IReadOnlyList<IssueReference>>.Fail(new Error("GitHub.Unexpected", "boom"));

        // Act
        await sut.GetTrackerContextAsync(
            await SeedRepoAsync(), IssueNumber, Content, TestContext.Current.CancellationToken);

        // Assert
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("owner/repo", StringComparison.Ordinal)
            && entry.Message.Contains("#7", StringComparison.Ordinal)
            && entry.Message.Contains("GitHub.Unexpected", StringComparison.Ordinal)
            && entry.Message.Contains("boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenTheLinksReadThrows_WarnsWithTheRepositoryTheIssueAndTheException()
    {
        // Arrange
        CapturingLogger<TrackerContextReader> logger = new();
        ITrackerContextQueries sut = SutWith(logger);
        _provider.LinksException = new HttpRequestException("connection reset");

        // Act
        await sut.GetTrackerContextAsync(
            await SeedRepoAsync(), IssueNumber, Content, TestContext.Current.CancellationToken);

        // Assert
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("owner/repo", StringComparison.Ordinal)
            && entry.Message.Contains("#7", StringComparison.Ordinal)
            && entry.Message.Contains("connection reset", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenTheLinksReadFails_FlagsItAndStillReadsTheMentions()
    {
        // Arrange
        _provider.Links = Result<IReadOnlyList<IssueReference>>.Fail(new Error("GitHub.Unexpected", "boom"));
        _provider.ReferencedIssues[9] = Result<ProviderReferencedIssue>.Ok(ReferencedRead("Mentioned"));
        IssueContent content = new IssueContentBuilder().WithBody("See #9.").Build();

        // Act
        TrackerContext context = await ReadAsync(content: content);

        // Assert
        context.ShouldSatisfyAllConditions(
            () => context.LinksUnreadable.ShouldBeTrue(),
            () => context.ReferencedIssues.Select(issue => issue.Number).ShouldBe([9]));
    }

    [Fact]
    public async Task WhenTheLinksReadThrows_FlagsItAndStillReadsTheMentions()
    {
        // Arrange
        _provider.LinksException = new HttpRequestException("connection reset");
        _provider.ReferencedIssues[9] = Result<ProviderReferencedIssue>.Ok(ReferencedRead("Mentioned"));
        IssueContent content = new IssueContentBuilder().WithBody("See #9.").Build();

        // Act
        TrackerContext context = await ReadAsync(content: content);

        // Assert
        context.ShouldSatisfyAllConditions(
            () => context.LinksUnreadable.ShouldBeTrue(),
            () => context.ReferencedIssues.Select(issue => issue.Number).ShouldBe([9]));
    }

    [Fact]
    public async Task WhenTheRepositoryCannotBeResolved_FlagsTheLinksListsEveryMentionAsUnreadableAndLeavesTheIndexNull()
    {
        // Arrange
        IssueContent content = new IssueContentBuilder().WithBody("See #9 and #4.").Build();

        // Act
        TrackerContext context = await _sut.GetTrackerContextAsync(
            new MonitoredRepositoryId(Guid.NewGuid()),
            IssueNumber,
            content,
            TestContext.Current.CancellationToken);

        // Assert
        context.ShouldSatisfyAllConditions(
            () => context.LinksUnreadable.ShouldBeTrue(),
            () => context.ReferencedIssues.ShouldBeEmpty(),
            () => context.UnreadableIssueNumbers.ShouldBe([9, 4]),
            () => context.OpenIssues.ShouldBeNull());
    }

    [Fact]
    public async Task WhenCreatingTheProviderThrows_FlagsTheLinksListsEveryMentionAsUnreadableAndLeavesTheIndexNull()
    {
        // Arrange
        ITrackerContextQueries sut = new TrackerContextReader(
            _dbContext,
            new ThrowingProviderFactory(),
            new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance),
            NullLogger<TrackerContextReader>.Instance);
        IssueContent content = new IssueContentBuilder().WithBody("See #9 and #4.").Build();

        // Act
        TrackerContext context = await sut.GetTrackerContextAsync(
            await SeedRepoAsync(), IssueNumber, content, TestContext.Current.CancellationToken);

        // Assert
        context.ShouldSatisfyAllConditions(
            () => context.LinksUnreadable.ShouldBeTrue(),
            () => context.ReferencedIssues.ShouldBeEmpty(),
            () => context.UnreadableIssueNumbers.ShouldBe([9, 4]),
            () => context.OpenIssues.ShouldBeNull());
    }

    [Fact]
    public async Task WhenTheProviderListsOpenIssues_ReturnsThemInOrderWithNoReferencedIssuesOrNotes()
    {
        // Arrange
        _provider.OpenIssues = Result<IReadOnlyList<OpenIssue>>.Ok(
            [new OpenIssue(30, "Newest"), new OpenIssue(20, "Middle"), new OpenIssue(10, "Oldest")]);

        // Act
        TrackerContext context = await ReadAsync();

        // Assert
        context.ShouldSatisfyAllConditions(
            () => context.OpenIssues.ShouldBe(
                [new OpenIssue(30, "Newest"), new OpenIssue(20, "Middle"), new OpenIssue(10, "Oldest")]),
            () => context.ReferencedIssues.ShouldBeEmpty(),
            () => context.UnreadableIssueNumbers.ShouldBeEmpty(),
            () => context.LinksUnreadable.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenTheProviderListsTheIssueItself_LeavesItOut()
    {
        // Arrange
        _provider.OpenIssues = Result<IReadOnlyList<OpenIssue>>.Ok(
            [new OpenIssue(8, "Newer"), new OpenIssue(IssueNumber, "Itself"), new OpenIssue(6, "Older")]);

        // Act
        TrackerContext context = await ReadAsync();

        // Assert
        context.OpenIssues.ShouldBe([new OpenIssue(8, "Newer"), new OpenIssue(6, "Older")]);
    }

    [Fact]
    public async Task WhenTheProviderListsMoreOpenIssuesThanTheCap_KeepsTheFirstOnesInOrder()
    {
        // Arrange
        List<OpenIssue> listed = [.. Enumerable.Range(1000, OpenIssueCap + 1).Select(number => new OpenIssue(number, $"Issue {number}"))];
        _provider.OpenIssues = Result<IReadOnlyList<OpenIssue>>.Ok(listed);

        // Act
        TrackerContext context = await ReadAsync();

        // Assert
        context.OpenIssues.ShouldBe(listed.Take(OpenIssueCap));
    }

    [Fact]
    public async Task WhenTheOpenIssueReadFails_LeavesTheIndexNull()
    {
        // Arrange
        _provider.OpenIssues = Result<IReadOnlyList<OpenIssue>>.Fail(new Error("GitHub.Unexpected", "boom"));

        // Act
        TrackerContext context = await ReadAsync();

        // Assert
        context.OpenIssues.ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheProviderDoesNotSupportTheOpenIssueRead_LeavesTheIndexNull()
    {
        // Arrange
        UnsupportedProvider provider = new();
        ITrackerContextQueries sut = new TrackerContextReader(
            _dbContext,
            new SingleIssueProviderFactory(provider),
            new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance),
            NullLogger<TrackerContextReader>.Instance);

        // Act
        TrackerContext context = await sut.GetTrackerContextAsync(
            await SeedRepoAsync(), IssueNumber, Content, TestContext.Current.CancellationToken);

        // Assert
        context.OpenIssues.ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheProviderThrows_LeavesTheIndexNullAndDoesNotThrow()
    {
        // Arrange
        _provider.OpenIssuesException = new HttpRequestException("connection reset");

        // Act
        TrackerContext context = await ReadAsync();

        // Assert
        context.OpenIssues.ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheCallersTokenIsCancelled_PropagatesTheCancellation()
    {
        // Arrange
        using CancellationTokenSource cancellation = new();
        _provider.CancelOnRead = cancellation;

        // Act
        Task<TrackerContext> read = ReadAsync(cancellation.Token);

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(async () => await read);
    }

    // Only the open-issue read is exercised; the rest are never reached by GetTrackerContextAsync.
    private class ThrowingProvider : IIssueProvider
    {
        public Task<Result<IssueListing>> GetIssuesAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(RepositorySlug slug, int issueNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> IsIssueClosedAsync(RepositorySlug slug, int issueNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(RepositorySlug slug, string pullRequestUrl, DateTimeOffset since, PostedCommentIds postedComments, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<BranchProtection>> GetBranchProtectionAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result> ApplyBranchRulesAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> CreateBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> DeleteBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> CanPushAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ProviderChecks>> GetChecksAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnsupportedProvider : ThrowingProvider;

    private sealed class ThrowingProviderFactory : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) =>
            throw new InvalidOperationException("no provider");
    }

    private sealed class TrackerReadsProvider : ThrowingProvider, IIssueProvider
    {
        public Result<IReadOnlyList<OpenIssue>> OpenIssues { get; set; } = Result<IReadOnlyList<OpenIssue>>.Ok([]);

        public Exception? OpenIssuesException { get; set; }

        public Result<IReadOnlyList<IssueReference>> Links { get; set; } = Result<IReadOnlyList<IssueReference>>.Ok([]);

        public Exception? LinksException { get; set; }

        public Dictionary<int, Result<ProviderReferencedIssue>> ReferencedIssues { get; } = [];

        public Task<Result<IReadOnlyList<IssueReference>>> GetIssueLinksAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken) =>
            LinksException is { } exception
                ? throw exception
                : Task.FromResult(Links);

        public HashSet<int> ThrowingReferencedIssues { get; } = [];

        public Task<Result<ProviderReferencedIssue>> GetReferencedIssueAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken) =>
            ThrowingReferencedIssues.Contains(issueNumber)
                ? throw new HttpRequestException("connection reset")
                : Task.FromResult(ReferencedIssues.GetValueOrDefault(
                    issueNumber,
                    Result<ProviderReferencedIssue>.Fail(new Error("GitHub.NotFound", "not found"))));

        public CancellationTokenSource? CancelOnRead { get; set; }

        public async Task<Result<IReadOnlyList<OpenIssue>>> GetOpenIssuesAsync(RepositorySlug slug, CancellationToken cancellationToken)
        {
            if (CancelOnRead is not null)
            {
                await CancelOnRead.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return OpenIssuesException is { } exception
                ? throw exception
                : OpenIssues;
        }
    }
}
