using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features;
using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.UpdateAuthModeTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    // ---------------------------------------------------------------------------
    // Stubs
    // ---------------------------------------------------------------------------

    private sealed class StubUsagePollCoordinator : IUsagePollCoordinator
    {
        public int CallCount { get; private set; }

        public Task<UsageSnapshot?> PollAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<UsageSnapshot?>(null);
        }
    }

    /// <summary>
    /// A coordinator whose PollAsync never completes until the test releases it,
    /// used to verify that HandleAsync does not block on the poll (fire-and-forget).
    /// </summary>
    private sealed class BlockingUsagePollCoordinator : IUsagePollCoordinator
    {
        private readonly TaskCompletionSource<UsageSnapshot?> _tcs = new();

        public void Release() => _tcs.SetResult(null);

        public Task<UsageSnapshot?> PollAsync(CancellationToken cancellationToken)
            => _tcs.Task;
    }

    private sealed class SpyUsageSnapshotStateMutator : IUsageSnapshotStateMutator
    {
        public int ClearCallCount { get; private set; }

        public void SetSnapshot(UsageSnapshot snapshot) { }
        public void MarkRefreshFailed(DateTimeOffset failedAt) { }
        public void Clear() => ClearCallCount++;
    }

    private static UpdateAuthMode.Handler BuildSut(
        FoundryDbContext dbContext,
        SpyUsageSnapshotStateMutator? mutator = null,
        IUsagePollCoordinator? coordinator = null)
        => new(dbContext, mutator ?? new SpyUsageSnapshotStateMutator(), coordinator ?? new StubUsagePollCoordinator());

    [Fact]
    public async Task WhenSwitchingToApiKeyMode_UpdatesAuthModeAndReturnsSummary()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = BuildSut(dbContext);

        UpdateAuthMode.Command command = new("api_key", "sk-ant-abc123");

        // Act
        Result<ClaudeAccountSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccountSummary summary = result.ShouldBeOfType<Result<ClaudeAccountSummary>.Success>().Value;
        summary.AuthMode.ShouldBe("ApiKey");
    }

    [Fact]
    public async Task WhenSwitchingToOAuthMode_SetsConfiguredMarkerWithNoScan()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.Present("old-key")));
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = BuildSut(dbContext);

        UpdateAuthMode.Command command = new("oauth", null);

        // Act
        Result<ClaudeAccountSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccountSummary summary = result.ShouldBeOfType<Result<ClaudeAccountSummary>.Success>().Value;
        summary.AuthMode.ShouldBe("OAuth");
    }

    [Fact]
    public async Task WhenSwitchingToOAuthMode_PersistsOAuthMarkerWithoutSubscriptionType()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateAuthMode.Handler sut = BuildSut(dbContext);
            UpdateAuthMode.Command command = new("oauth", null);
            await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        ClaudeAccount persisted = (await assertDb.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken))!;
        AuthMode.OAuth oauth = persisted.AuthMode.ShouldBeOfType<AuthMode.OAuth>();
        oauth.SubscriptionType.ShouldBeNull();
    }

    [Fact]
    public async Task WhenAccountNotFound_ReturnsNotFoundError()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = BuildSut(dbContext);

        UpdateAuthMode.Command command = new("api_key", "sk-ant-abc123");

        // Act
        Result<ClaudeAccountSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<ClaudeAccountSummary>.Failure failure =
            result.ShouldBeOfType<Result<ClaudeAccountSummary>.Failure>();
        failure.Error.Code.ShouldBe(CredentialsErrors.NotFoundCode);
    }

    [Fact]
    public async Task WhenSwitchingToApiKeyMode_PersistsChangesToDatabase()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount seed = ClaudeAccount.Create();
            seed.SetAuthMode(new AuthMode.OAuth(SubscriptionType: null));
            seedDb.Set<ClaudeAccount>().Add(seed);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateAuthMode.Handler sut = BuildSut(dbContext);
            UpdateAuthMode.Command command = new("api_key", "sk-ant-new-key");
            await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        ClaudeAccount account = (await assertDb.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken))!;
        account.AuthMode.ShouldBeOfType<AuthMode.ApiKey>();
    }

    [Fact]
    public async Task WhenSwitchingToApiKeyMode_ClearsOAuthIdentityAndSetsValidValidity()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount seed = ClaudeAccount.Create();
            seed.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");
            seedDb.Set<ClaudeAccount>().Add(seed);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateAuthMode.Handler sut = BuildSut(dbContext);
            UpdateAuthMode.Command command = new("api_key", "sk-ant-new-key");
            await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        ClaudeAccount account = (await assertDb.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken))!;
        account.ShouldSatisfyAllConditions(
            () => account.OAuthAccountEmail.ShouldBeNull(),
            () => account.OAuthAccountOrgName.ShouldBeNull(),
            () => account.Validity.ShouldBeOfType<CredentialValidity.Valid>());
    }

    [Fact]
    public async Task WhenAuthModeChanges_ClearsUsageSnapshot()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        SpyUsageSnapshotStateMutator mutator = new();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = BuildSut(dbContext, mutator);

        UpdateAuthMode.Command command = new("oauth", null);

        // Act
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        mutator.ClearCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenSwitchingToOAuthWithCommittedIdentity_TriggersPoll()
    {
        // Arrange — OAuth + committed identity + valid = CanReportUsage true → poll triggered.
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            // RecordSuccessfulLogin sets OAuth mode + committed identity + valid.
            account.RecordSuccessfulLogin("user@example.com", "Acme Corp", "pro");
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        StubUsagePollCoordinator coordinator = new();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = BuildSut(dbContext, coordinator: coordinator);

        // Switching OAuth→OAuth (e.g. after a session ends and re-configured) still clears + polls.
        UpdateAuthMode.Command command = new("oauth", null);

        // Act
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        coordinator.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenSwitchingToOAuthModeWithoutCommittedIdentity_DoesNotTriggerPoll()
    {
        // Arrange — OAuth without committed identity → CanReportUsage false → no poll.
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        StubUsagePollCoordinator coordinator = new();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = BuildSut(dbContext, coordinator: coordinator);

        UpdateAuthMode.Command command = new("oauth", null);

        // Act
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        coordinator.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenSwitchingToApiKeyMode_DoesNotTriggerPoll()
    {
        // Arrange — API-key mode never yields CanReportUsage=true; no poll should be triggered.
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.SetAuthMode(new AuthMode.OAuth(SubscriptionType: null));
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        StubUsagePollCoordinator coordinator = new();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = BuildSut(dbContext, coordinator: coordinator);

        UpdateAuthMode.Command command = new("api_key", "sk-ant-new-key");

        // Act
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        coordinator.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenPollWouldBlock_HandlerReturnsWithoutWaitingForPoll()
    {
        // Arrange — OAuth + committed identity so CanReportUsage is true; coordinator blocks
        // until released. If the handler awaits the poll, this test hangs.
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.RecordSuccessfulLogin("user@example.com", "Acme Corp", "pro");
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        BlockingUsagePollCoordinator coordinator = new();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAuthMode.Handler sut = BuildSut(dbContext, coordinator: coordinator);
        UpdateAuthMode.Command command = new("oauth", null);

        // Act — must complete even though coordinator never resolves
        Task<Result<ClaudeAccountSummary>> handleTask = sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        Task completed = await Task.WhenAny(handleTask, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        // Assert — handler completed before the 2s timeout (i.e., did not block on poll)
        completed.ShouldBeSameAs(handleTask);

        // Release the background task to avoid dangling tasks after the test.
        coordinator.Release();
    }
}
