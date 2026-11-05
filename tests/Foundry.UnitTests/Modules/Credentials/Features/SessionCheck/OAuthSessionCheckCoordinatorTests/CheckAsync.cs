using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Features.SessionCheck;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Credentials;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.SessionCheck.OAuthSessionCheckCoordinatorTests;

public sealed class CheckAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FakeCredentialsOrchestrator _orchestrator = new();
    private readonly FakeLoginSessionState _loginState = new();
    private readonly CapturingIntegrationEventDispatcher _dispatcher = new();
    private readonly ServiceProvider _serviceProvider;

    public CheckAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        ServiceCollection services = new();
        services.AddDbContext<FoundryDbContext>((_, options) => options.UseSqlite(_connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        services.AddSingleton<IIntegrationEventDispatcher>(_dispatcher);
        _serviceProvider = services.BuildServiceProvider();

        using IServiceScope setup = _serviceProvider.CreateScope();
        setup.ServiceProvider.GetRequiredService<FoundryDbContext>().Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class CapturingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        private readonly List<IIntegrationEvent> _captured = [];

        public IReadOnlyList<IIntegrationEvent> Captured => _captured;

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            _captured.AddRange(events);
            return Task.CompletedTask;
        }
    }

    private OAuthSessionCheckCoordinator BuildSut(ICredentialsOrchestrator? orchestrator = null) => new(
        _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
        orchestrator ?? _orchestrator,
        _loginState,
        NullLogger<OAuthSessionCheckCoordinator>.Instance);

    private async Task SeedAccountAsync(Action<ClaudeAccount> configure)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        ClaudeAccount account = ClaudeAccount.Create();
        configure(account);
        db.Set<ClaudeAccount>().Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private Task SeedOAuthAccountAsync() =>
        SeedAccountAsync(a => a.RecordSuccessfulLogin("user@example.com", "Org", "max"));

    private async Task<ClaudeAccount> LoadAccountAsync()
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        return await db.Set<ClaudeAccount>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task<bool> CanDispatchAsync()
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        CredentialGate gate = new(scope.ServiceProvider.GetRequiredService<DbContext>(), _loginState);
        return await gate.CanDispatchAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenRefreshRejected_InvalidatesNamingTheExpiredSessionAndPublishesCredentialsInvalidated()
    {
        // Arrange
        await SeedOAuthAccountAsync();
        _orchestrator.WithSessionStatus(new OAuthSessionStatus.RefreshRejected());

        // Act
        OAuthSessionStatus? status = await BuildSut().CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        status.ShouldBeOfType<OAuthSessionStatus.RefreshRejected>();
        ClaudeAccount account = await LoadAccountAsync();
        CredentialValidity.Invalid invalid = account.Validity.ShouldBeOfType<CredentialValidity.Invalid>();
        invalid.Reason.ShouldBe(OAuthSessionCheckCoordinator.RefreshRejectedReason);
        invalid.Reason.ShouldContain("session expired");
        CredentialsInvalidated published = _dispatcher.Captured.ShouldHaveSingleItem()
            .ShouldBeOfType<CredentialsInvalidated>();
        published.Reason.ShouldBe(OAuthSessionCheckCoordinator.RefreshRejectedReason);
    }

    [Fact]
    public async Task WhenRefreshRejected_CredentialGateBlocksDispatch()
    {
        // Arrange
        await SeedOAuthAccountAsync();
        (await CanDispatchAsync()).ShouldBeTrue();
        _orchestrator.WithSessionStatus(new OAuthSessionStatus.RefreshRejected());

        // Act
        await BuildSut().CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        (await CanDispatchAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenOperatorSignsInAgain_AccountIsValidAndTheNextCheckLeavesItValid()
    {
        // Arrange
        await SeedOAuthAccountAsync();
        _orchestrator.WithSessionStatus(new OAuthSessionStatus.RefreshRejected());
        OAuthSessionCheckCoordinator sut = BuildSut();
        await sut.CheckAsync(TestContext.Current.CancellationToken);

        await using (AsyncServiceScope scope = _serviceProvider.CreateAsyncScope())
        {
            FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            ClaudeAccount tracked = await db.Set<ClaudeAccount>().SingleAsync(TestContext.Current.CancellationToken);
            tracked.RecordSuccessfulLogin("user@example.com", "Org", "max");
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _orchestrator.WithSessionStatus(new OAuthSessionStatus.Active());

        // Act
        OAuthSessionStatus? status = await sut.CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        status.ShouldBeOfType<OAuthSessionStatus.Active>();
        (await LoadAccountAsync()).Validity.ShouldBeOfType<CredentialValidity.Valid>();
        (await CanDispatchAsync()).ShouldBeTrue();
        _dispatcher.Captured.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenNotRefreshable_InvalidatesWithNotRefreshableReason()
    {
        // Arrange
        await SeedOAuthAccountAsync();
        _orchestrator.WithSessionStatus(new OAuthSessionStatus.NotRefreshable());

        // Act
        await BuildSut().CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync()).Validity.ShouldBe(
            new CredentialValidity.Invalid(OAuthSessionCheckCoordinator.NotRefreshableReason));
    }

    [Fact]
    public async Task WhenSignedOut_InvalidatesWithSignedOutReason()
    {
        // Arrange
        await SeedOAuthAccountAsync();
        _orchestrator.WithSessionStatus(new OAuthSessionStatus.SignedOut());

        // Act
        await BuildSut().CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync()).Validity.ShouldBe(
            new CredentialValidity.Invalid(OAuthSessionCheckCoordinator.SignedOutReason));
    }

    [Theory]
    [InlineData(nameof(OAuthSessionStatus.Active))]
    [InlineData(nameof(OAuthSessionStatus.Expired))]
    [InlineData(nameof(OAuthSessionStatus.Unreadable))]
    public async Task WhenStatusIsNotTerminal_LeavesAccountValid(string statusName)
    {
        // Arrange
        OAuthSessionStatus scripted = statusName switch
        {
            nameof(OAuthSessionStatus.Active) => new OAuthSessionStatus.Active(),
            nameof(OAuthSessionStatus.Expired) => new OAuthSessionStatus.Expired(),
            _ => new OAuthSessionStatus.Unreadable(),
        };
        await SeedOAuthAccountAsync();
        _orchestrator.WithSessionStatus(scripted);

        // Act
        OAuthSessionStatus? status = await BuildSut().CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        status.ShouldBe(scripted);
        (await LoadAccountAsync()).Validity.ShouldBeOfType<CredentialValidity.Valid>();
        _dispatcher.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenCheckFailsTransiently_LeavesAccountValidAndRetriesOnNextTick()
    {
        // Arrange
        await SeedOAuthAccountAsync();
        _orchestrator.WithSessionCheckFailure(new Error("Docker.SessionCheckFailed", "connection refused"));
        OAuthSessionCheckCoordinator sut = BuildSut();

        // Act
        OAuthSessionStatus? first = await sut.CheckAsync(TestContext.Current.CancellationToken);
        OAuthSessionStatus? second = await sut.CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        first.ShouldBeNull();
        second.ShouldBeNull();
        _orchestrator.SessionCheckCallCount.ShouldBe(2);
        (await LoadAccountAsync()).Validity.ShouldBeOfType<CredentialValidity.Valid>();
        _dispatcher.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNoAccount_SkipsTheCheck()
    {
        // Act
        OAuthSessionStatus? status = await BuildSut().CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        status.ShouldBeNull();
        _orchestrator.SessionCheckCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenApiKeyAccount_SkipsTheCheck()
    {
        // Arrange
        await SeedAccountAsync(a => a.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.Present("sk-ant-api"))));

        // Act
        OAuthSessionStatus? status = await BuildSut().CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        status.ShouldBeNull();
        _orchestrator.SessionCheckCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenOAuthModeWithoutCommittedLogin_SkipsTheCheck()
    {
        // Arrange
        await SeedAccountAsync(a => a.SetAuthMode(new AuthMode.OAuth(null)));

        // Act
        OAuthSessionStatus? status = await BuildSut().CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        status.ShouldBeNull();
        _orchestrator.SessionCheckCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenAccountAlreadyInvalid_SkipsTheCheck()
    {
        // Arrange
        await SeedAccountAsync(a =>
        {
            a.RecordSuccessfulLogin("user@example.com", "Org", "max");
            a.Invalidate("worker auth failure");
        });

        // Act
        OAuthSessionStatus? status = await BuildSut().CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        status.ShouldBeNull();
        _orchestrator.SessionCheckCallCount.ShouldBe(0);
        _dispatcher.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenLoginActive_SkipsTheCheck()
    {
        // Arrange
        await SeedOAuthAccountAsync();
        _loginState.IsLoginActive = true;

        // Act
        OAuthSessionStatus? status = await BuildSut().CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        status.ShouldBeNull();
        _orchestrator.SessionCheckCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenLoginStartsDuringTheCheck_DoesNotInvalidate()
    {
        // Arrange
        await SeedOAuthAccountAsync();
        _orchestrator
            .WithSessionStatus(new OAuthSessionStatus.RefreshRejected())
            .OnSessionCheck(() =>
            {
                _loginState.IsLoginActive = true;
                return Task.CompletedTask;
            });

        // Act
        await BuildSut().CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync()).Validity.ShouldBeOfType<CredentialValidity.Valid>();
        _dispatcher.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenAccountChangesDuringTheCheck_DoesNotInvalidate()
    {
        // Arrange — a login commits fresh credentials while the helper container runs.
        await SeedOAuthAccountAsync();
        _orchestrator
            .WithSessionStatus(new OAuthSessionStatus.RefreshRejected())
            .OnSessionCheck(async () =>
            {
                await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
                FoundryDbContext db = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
                ClaudeAccount tracked = await db.Set<ClaudeAccount>().SingleAsync();
                await Task.Delay(5);
                tracked.RecordSuccessfulLogin("other@example.com", "Org", "max");
                await db.SaveChangesAsync();
            });

        // Act
        await BuildSut().CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccount account = await LoadAccountAsync();
        account.Validity.ShouldBeOfType<CredentialValidity.Valid>();
        account.OAuthAccountEmail.ShouldBe("other@example.com");
        _dispatcher.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenCheckAlreadyInFlight_SecondCallerSkips()
    {
        // Arrange
        await SeedOAuthAccountAsync();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _orchestrator.OnSessionCheck(async () =>
        {
            entered.TrySetResult();
            await release.Task;
        });
        OAuthSessionCheckCoordinator sut = BuildSut();

        // Act
        Task<OAuthSessionStatus?> first = sut.CheckAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        OAuthSessionStatus? second = await sut.CheckAsync(TestContext.Current.CancellationToken);
        release.SetResult();
        OAuthSessionStatus? firstResult = await first;

        // Assert
        second.ShouldBeNull();
        firstResult.ShouldBeOfType<OAuthSessionStatus.Active>();
        _orchestrator.SessionCheckCallCount.ShouldBe(1);
    }
}
