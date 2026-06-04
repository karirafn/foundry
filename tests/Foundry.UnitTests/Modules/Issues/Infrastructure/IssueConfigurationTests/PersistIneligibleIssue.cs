using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistIneligibleIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistIneligibleIssue()
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

    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    [Fact]
    public async Task WhenIneligibleIssuePersisted_CanBeReloadedAsIneligibleIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 42,
            title: "Ineligible issue",
            body: "Body text",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["foundry"],
            detectedAt: DateTimeOffset.UtcNow);

        IReadOnlyList<EligibilityViolation> violations =
        [
            EligibilityViolation.AllowDirectPushes(),
            EligibilityViolation.AllowForcePushes()
        ];

        IneligibleIssue ineligible = IneligibleIssue.FromDetected(detected, violations);

        _dbContext.Set<Issue>().Add(ineligible);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([ineligible.Id], TestContext.Current.CancellationToken);

        // Assert
        IneligibleIssue reloaded = result.ShouldBeOfType<IneligibleIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.Id.ShouldBe(ineligible.Id),
            () => reloaded.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => reloaded.IssueNumber.ShouldBe(42),
            () => reloaded.Title.ShouldBe("Ineligible issue"),
            () => reloaded.Violations.Count.ShouldBe(2));
    }

    [Fact]
    public async Task WhenIneligibleIssuePersisted_ViolationsRulesArePreserved()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Test",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        IReadOnlyList<EligibilityViolation> violations = [EligibilityViolation.AllowDirectPushes()];
        IneligibleIssue ineligible = IneligibleIssue.FromDetected(detected, violations);

        _dbContext.Set<Issue>().Add(ineligible);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([ineligible.Id], TestContext.Current.CancellationToken);

        // Assert
        IneligibleIssue reloaded = result.ShouldBeOfType<IneligibleIssue>();
        EligibilityViolation violation = reloaded.Violations.ShouldHaveSingleItem();
        violation.Rule.ShouldBe(EligibilityViolation.AllowDirectPushes().Rule);
    }
}
