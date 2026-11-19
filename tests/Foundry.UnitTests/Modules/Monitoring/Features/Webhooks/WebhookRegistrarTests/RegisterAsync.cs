using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.WebhookRegistrarTests;

public sealed class RegisterAsync
{
    private const string PublicBaseUrl = "https://hooks.example.com";
    private const string AccountToken = "github_pat_account";
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private static WebhookRegistrar BuildSut(
        RecordingBranchRulesProviderFactory factory,
        string? publicBaseUrl = PublicBaseUrl,
        TimeProvider? timeProvider = null,
        ILogger<WebhookRegistrar>? logger = null) =>
        new(
            new WebhookIngress(
                new StubWebhookSettingsQueries(publicBaseUrl)),
            factory,
            timeProvider ?? TimeProvider.System,
            logger ?? NullLogger<WebhookRegistrar>.Instance);

    private static MonitoredRepository NewRepository(string slug = "owner/repo") =>
        MonitoredRepository.Create(RepositorySlug.Create(slug).ValueOrThrow(), "github.com", null).ValueOrThrow();

    private static GitHubCredential GitHubAccount(string? accountToken = AccountToken) =>
        GitHubCredential.Create(
            AccountName.Create("owner").ValueOrThrow(),
            accountToken is null ? null : new IssuedToken(accountToken, new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://github.com").ValueOrThrow());

    [Fact]
    public async Task WhenNotRegisteredAndTokenPresent_CreatesTheHookWithTheAccountTokenAndBecomesPending()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new();
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository();

        // Act
        WebhookRegistrationOutcome outcome = await sut.RegisterAsync(repository, GitHubAccount(), CancellationToken.None);

        // Assert
        outcome.Kind.ShouldBe(WebhookRegistrationOutcome.CreatedKind);
        repository.WebhookStatus.ShouldBe(WebhookStatus.Pending);
        factory.TokensUsed.ShouldBe([AccountToken]);
        factory.WebhookUpserts.Count.ShouldBe(1);
        WebhookHookSpec spec = factory.WebhookUpserts[0].Spec;
        spec.PayloadUrl.ShouldBe($"{PublicBaseUrl}/webhooks/github/{repository.WebhookHookId}");
        spec.Secret.ShouldBe(repository.EnsureWebhookSecret());
        spec.HookPath.Suffix.ShouldBe($"/webhooks/github/{repository.WebhookHookId}");
    }

    [Fact]
    public async Task ForAGitLabAccount_UsesTheGitLabPayloadPath()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new();
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository("group/project");
        GitLabCredential account = GitLabCredential.Create(
            AccountName.Create("group").ValueOrThrow(), new IssuedToken("glpat_account", new TokenExpiry.NotChecked()), BaseUrl.Create("https://gitlab.com").ValueOrThrow());

        // Act
        WebhookRegistrationOutcome outcome = await sut.RegisterAsync(repository, account, CancellationToken.None);

        // Assert
        outcome.IsRegistered.ShouldBeTrue();
        factory.TokensUsed.ShouldBe(["glpat_account"]);
        factory.WebhookUpserts[0].Spec.PayloadUrl.ShouldBe($"{PublicBaseUrl}/webhooks/gitlab/{repository.WebhookHookId}");
    }

    [Fact]
    public async Task WhenAnExistingHookIsUpdated_ReportsUpdated()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithWebhookResult("owner/repo", Result<WebhookUpsert>.Ok(new WebhookUpsert(WebhookUpsertKind.Updated, 77)));
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository();

        // Act
        WebhookRegistrationOutcome outcome = await sut.RegisterAsync(repository, GitHubAccount(), CancellationToken.None);

        // Assert
        outcome.Kind.ShouldBe(WebhookRegistrationOutcome.UpdatedKind);
        repository.WebhookStatus.ShouldBe(WebhookStatus.Pending);
    }

    [Fact]
    public async Task WhenAlreadyActive_StaysActive()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new();
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository();
        repository.MarkWebhookRegistered("stamp");
        repository.RecordWebhookDelivery();

        // Act
        await sut.RegisterAsync(repository, GitHubAccount(), CancellationToken.None);

        // Assert
        repository.WebhookStatus.ShouldBe(WebhookStatus.Active);
    }

    [Fact]
    public async Task WhenTheAccountTokenIsAbsent_MakesNoCallAndReportsTokenUnreadable()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new();
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository();

        // Act
        WebhookRegistrationOutcome outcome = await sut.RegisterAsync(
            repository, GitHubAccount(accountToken: null), CancellationToken.None);

        // Assert
        outcome.ShouldBe(new WebhookRegistrationOutcome(
            WebhookRegistrationOutcome.SkippedKind, WebhookRegistrationOutcome.TokenUnreadableReason));
        factory.TokensUsed.ShouldBeEmpty();
        factory.WebhookUpserts.ShouldBeEmpty();
        repository.WebhookStatus.ShouldBe(WebhookStatus.NotRegistered);
    }

    [Fact]
    public async Task WhenTheAccountTokenIsUnreadable_MakesNoCallAndReportsTokenUnreadable()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new();
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository();
        GitHubCredential account = GitHubCredential.CreateWithUnreadableToken(
            AccountName.Create("owner").ValueOrThrow(), BaseUrl.Create("https://github.com").ValueOrThrow());

        // Act
        WebhookRegistrationOutcome outcome = await sut.RegisterAsync(repository, account, CancellationToken.None);

        // Assert
        outcome.ShouldBe(new WebhookRegistrationOutcome(
            WebhookRegistrationOutcome.SkippedKind, WebhookRegistrationOutcome.TokenUnreadableReason));
        factory.TokensUsed.ShouldBeEmpty();
        repository.WebhookStatus.ShouldBe(WebhookStatus.NotRegistered);
    }

    [Fact]
    public async Task WhenTheAccountTokenIsMarkedInvalid_StillAttemptsTheRegistration()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new();
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository();
        GitHubCredential account = GitHubAccount();
        account.MarkTokenInvalid(DateTimeOffset.UnixEpoch);

        // Act
        WebhookRegistrationOutcome outcome = await sut.RegisterAsync(repository, account, CancellationToken.None);

        // Assert
        outcome.Kind.ShouldBe(WebhookRegistrationOutcome.CreatedKind);
        factory.TokensUsed.ShouldBe([AccountToken]);
    }

    [Fact]
    public async Task WhenNoPublicBaseUrlIsStored_MakesNoCall()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new();
        WebhookRegistrar sut = BuildSut(factory, publicBaseUrl: null);
        MonitoredRepository repository = NewRepository();

        // Act
        WebhookRegistrationOutcome outcome = await sut.RegisterAsync(repository, GitHubAccount(), CancellationToken.None);

        // Assert
        outcome.Reason.ShouldBe(WebhookRegistrationOutcome.NotConfiguredReason);
        factory.TokensUsed.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenPermissionIsMissing_FailsWithInsufficientPermissionAndStaysNotRegistered()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithWebhookResult("owner/repo", Result<WebhookUpsert>.Fail(WebhookWriteErrors.InsufficientPermission("403")));
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository();

        // Act
        WebhookRegistrationOutcome outcome = await sut.RegisterAsync(repository, GitHubAccount(), CancellationToken.None);

        // Assert
        outcome.Kind.ShouldBe(WebhookRegistrationOutcome.FailedKind);
        outcome.Reason.ShouldBe(WebhookRegistrationOutcome.InsufficientPermissionReason);
        repository.WebhookStatus.ShouldBe(WebhookStatus.NotRegistered);
    }

    [Fact]
    public async Task WhenTheProviderFailsOtherwise_FailsWithProviderError()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithWebhookResult("owner/repo", Result<WebhookUpsert>.Fail(new Error("GitHub.ProviderError", "500")));
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository();

        // Act
        WebhookRegistrationOutcome outcome = await sut.RegisterAsync(repository, GitHubAccount(), CancellationToken.None);

        // Assert
        outcome.Reason.ShouldBe(WebhookRegistrationOutcome.ProviderErrorReason);
        repository.WebhookStatus.ShouldBe(WebhookStatus.NotRegistered);
    }

    [Fact]
    public async Task WhenTheProviderThrows_FailsWithProviderError()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithException("owner/repo", new HttpRequestException("boom"));
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository();

        // Act
        WebhookRegistrationOutcome outcome = await sut.RegisterAsync(repository, GitHubAccount(), CancellationToken.None);

        // Assert
        outcome.Reason.ShouldBe(WebhookRegistrationOutcome.ProviderErrorReason);
        outcome.Detail.ShouldBe(WebhookRegistrar.UnreachableDetail);
    }

    [Fact]
    public async Task WhenRegistered_StoresTheCurrentStampAndClearsAnEarlierFailure()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new();
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository();
        repository.RecordWebhookRegistrationFailed(Now);

        // Act
        await sut.RegisterAsync(repository, GitHubAccount(), CancellationToken.None);

        // Assert
        repository.WebhookSpecStamp.ShouldBe(WebhookSpecStamp.For(WebhookProvider.GitHub, PublicBaseUrl));
        repository.WebhookRegistrationFailedAt.ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheProviderFails_RecordsTheAttemptTimeAndKeepsTheStamp()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithWebhookResult("owner/repo", Result<WebhookUpsert>.Fail(new Error("GitHub.ProviderError", "500")));
        WebhookRegistrar sut = BuildSut(factory, timeProvider: new FakeTimeProvider(Now));
        MonitoredRepository repository = NewRepository();
        repository.MarkWebhookRegistered("old-stamp");

        // Act
        await sut.RegisterAsync(repository, GitHubAccount(), CancellationToken.None);

        // Assert
        repository.WebhookRegistrationFailedAt.ShouldBe(Now);
        repository.WebhookSpecStamp.ShouldBe("old-stamp");
    }

    [Fact]
    public async Task WhenPermissionIsMissing_RecordsTheAttemptTime()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithWebhookResult("owner/repo", Result<WebhookUpsert>.Fail(WebhookWriteErrors.InsufficientPermission("403")));
        WebhookRegistrar sut = BuildSut(factory, timeProvider: new FakeTimeProvider(Now));
        MonitoredRepository repository = NewRepository();

        // Act
        await sut.RegisterAsync(repository, GitHubAccount(), CancellationToken.None);

        // Assert
        repository.WebhookRegistrationFailedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task WhenSkipped_RecordsNothing()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new();
        WebhookRegistrar sut = BuildSut(factory, timeProvider: new FakeTimeProvider(Now));
        MonitoredRepository repository = NewRepository();

        // Act
        await sut.RegisterAsync(repository, GitHubAccount(accountToken: null), CancellationToken.None);

        // Assert
        repository.WebhookRegistrationFailedAt.ShouldBeNull();
        repository.WebhookSpecStamp.ShouldBeNull();
    }

    [Fact]
    public async Task WhenCreated_SendsATestDeliveryToTheCreatedHookWithTheAccountToken()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new();
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository();

        // Act
        await sut.RegisterAsync(repository, GitHubAccount(), CancellationToken.None);

        // Assert
        factory.WebhookTests.ShouldBe([("owner/repo", RecordingBranchRulesProviderFactory.DefaultProviderHookId)]);
        factory.TokensUsed.ShouldBe([AccountToken]);
    }

    [Fact]
    public async Task WhenUpdated_SendsATestDeliveryToTheUpdatedHook()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithWebhookResult("group/project", Result<WebhookUpsert>.Ok(new WebhookUpsert(WebhookUpsertKind.Updated, 77)));
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository("group/project");
        GitLabCredential account = GitLabCredential.Create(
            AccountName.Create("group").ValueOrThrow(), new IssuedToken("glpat_account", new TokenExpiry.NotChecked()), BaseUrl.Create("https://gitlab.com").ValueOrThrow());

        // Act
        await sut.RegisterAsync(repository, account, CancellationToken.None);

        // Assert
        factory.WebhookTests.ShouldBe([("group/project", 77L)]);
    }

    [Fact]
    public async Task WhenTheRegistrationFails_SendsNoTestDelivery()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithWebhookResult("owner/repo", Result<WebhookUpsert>.Fail(new Error("GitHub.ProviderError", "boom")));
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository();

        // Act
        await sut.RegisterAsync(repository, GitHubAccount(), CancellationToken.None);

        // Assert
        factory.WebhookTests.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheTestDeliveryFails_LogsAWarningAndKeepsTheRegistration()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithWebhookTestResult("owner/repo", Result.Fail(new Error("GitLab.RateLimitExhausted", "Too many requests.")));
        CapturingLogger<WebhookRegistrar> logger = new();
        WebhookRegistrar sut = BuildSut(factory, logger: logger);
        MonitoredRepository repository = NewRepository();

        // Act
        WebhookRegistrationOutcome outcome = await sut.RegisterAsync(repository, GitHubAccount(), CancellationToken.None);

        // Assert
        outcome.Kind.ShouldBe(WebhookRegistrationOutcome.CreatedKind);
        repository.WebhookStatus.ShouldBe(WebhookStatus.Pending);
        repository.WebhookSpecStamp.ShouldBe(WebhookSpecStamp.For(WebhookProvider.GitHub, PublicBaseUrl));
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("GitLab.RateLimitExhausted"));
    }

    [Fact]
    public async Task WhenTheTestDeliveryThrows_LogsAWarningAndKeepsTheRegistration()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithWebhookTestException("owner/repo", new HttpRequestException("boom"));
        CapturingLogger<WebhookRegistrar> logger = new();
        WebhookRegistrar sut = BuildSut(factory, logger: logger);
        MonitoredRepository repository = NewRepository();

        // Act
        WebhookRegistrationOutcome outcome = await sut.RegisterAsync(repository, GitHubAccount(), CancellationToken.None);

        // Assert
        outcome.Kind.ShouldBe(WebhookRegistrationOutcome.CreatedKind);
        repository.WebhookStatus.ShouldBe(WebhookStatus.Pending);
        repository.WebhookSpecStamp.ShouldBe(WebhookSpecStamp.For(WebhookProvider.GitHub, PublicBaseUrl));
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Exception is HttpRequestException);
    }
}
