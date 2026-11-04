using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.WebhookRegistrarTests;

public sealed class RegisterAsync
{
    private const string PublicBaseUrl = "https://hooks.example.com";
    private const string ServerToken = "github_pat_server_side";
    private const string WorkerToken = "ghp_worker";

    private static WebhookRegistrar BuildSut(
        RecordingBranchRulesProviderFactory factory,
        string? publicBaseUrl = PublicBaseUrl,
        int? port = 5099) =>
        new(
            new WebhookIngress(
                Options.Create(new WebhookIngressOptions { Port = port }),
                new StubWebhookSettingsQueries(publicBaseUrl)),
            factory,
            NullLogger<WebhookRegistrar>.Instance);

    private static MonitoredRepository NewRepository(string slug = "owner/repo") =>
        MonitoredRepository.Create(RepositorySlug.Create(slug).ValueOrThrow(), "github.com", null).ValueOrThrow();

    private static GitHubCredential GitHubAccount(string? serverToken = ServerToken) =>
        GitHubCredential.Create("owner", WorkerToken, BaseUrl.Create("https://github.com").ValueOrThrow(), serverToken);

    [Fact]
    public async Task WhenNotRegisteredAndTokenPresent_CreatesTheHookWithTheServerTokenAndBecomesPending()
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
        factory.TokensUsed.ShouldBe([ServerToken]);
        factory.WebhookUpserts.Count.ShouldBe(1);
        WebhookHookSpec spec = factory.WebhookUpserts[0].Spec;
        spec.PayloadUrl.ShouldBe($"{PublicBaseUrl}/webhooks/github/{repository.WebhookHookId}");
        spec.Secret.ShouldBe(repository.EnsureWebhookSecret());
        spec.PreviousPayloadUrls.ShouldBeEmpty();
    }

    [Fact]
    public async Task ForAGitLabAccount_UsesTheGitLabPayloadPath()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new();
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository("group/project");
        GitLabCredential account = GitLabCredential.Create(
            "group", "glpat_worker", BaseUrl.Create("https://gitlab.com").ValueOrThrow(), "glpat_server");

        // Act
        WebhookRegistrationOutcome outcome = await sut.RegisterAsync(repository, account, CancellationToken.None);

        // Assert
        outcome.IsRegistered.ShouldBeTrue();
        factory.TokensUsed.ShouldBe(["glpat_server"]);
        factory.WebhookUpserts[0].Spec.PayloadUrl.ShouldBe($"{PublicBaseUrl}/webhooks/gitlab/{repository.WebhookHookId}");
    }

    [Fact]
    public async Task WhenAnExistingHookIsUpdated_ReportsUpdated()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithWebhookResult("owner/repo", Result<WebhookUpsertKind>.Ok(WebhookUpsertKind.Updated));
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
        repository.MarkWebhookRegistered();
        repository.RecordWebhookDelivery();

        // Act
        await sut.RegisterAsync(repository, GitHubAccount(), CancellationToken.None);

        // Assert
        repository.WebhookStatus.ShouldBe(WebhookStatus.Active);
    }

    [Fact]
    public async Task WithAPreviousPublicBaseUrl_PassesThePreviousPayloadUrlToMatch()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new();
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository();

        // Act
        await sut.RegisterAsync(repository, GitHubAccount(), "https://old.example.com", CancellationToken.None);

        // Assert
        factory.WebhookUpserts[0].Spec.PreviousPayloadUrls
            .ShouldBe([$"https://old.example.com/webhooks/github/{repository.WebhookHookId}"]);
    }

    [Fact]
    public async Task WhenNoServerTokenIsConfigured_MakesNoCall()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new();
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository();

        // Act
        WebhookRegistrationOutcome outcome = await sut.RegisterAsync(
            repository, GitHubAccount(serverToken: null), CancellationToken.None);

        // Assert
        outcome.ShouldBe(new WebhookRegistrationOutcome(
            WebhookRegistrationOutcome.SkippedKind, WebhookRegistrationOutcome.NoServerTokenReason));
        factory.TokensUsed.ShouldBeEmpty();
        factory.WebhookUpserts.ShouldBeEmpty();
        repository.WebhookStatus.ShouldBe(WebhookStatus.NotRegistered);
    }

    [Fact]
    public async Task WhenTheServerTokenIsUnreadable_MakesNoCall()
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new();
        WebhookRegistrar sut = BuildSut(factory);
        MonitoredRepository repository = NewRepository();
        GitHubCredential account = GitHubAccount();
        Credential.ApplyUnreadableBranchRulesToken(account);

        // Act
        WebhookRegistrationOutcome outcome = await sut.RegisterAsync(repository, account, CancellationToken.None);

        // Assert
        outcome.Reason.ShouldBe(WebhookRegistrationOutcome.ServerTokenUnreadableReason);
        factory.TokensUsed.ShouldBeEmpty();
        repository.WebhookStatus.ShouldBe(WebhookStatus.NotRegistered);
    }

    [Theory]
    [InlineData(null, 5099)]
    [InlineData(PublicBaseUrl, null)]
    public async Task WhenIngressIsNotConfigured_MakesNoCall(string? publicBaseUrl, int? port)
    {
        // Arrange
        RecordingBranchRulesProviderFactory factory = new();
        WebhookRegistrar sut = BuildSut(factory, publicBaseUrl, port);
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
            .WithWebhookResult("owner/repo", Result<WebhookUpsertKind>.Fail(WebhookWriteErrors.InsufficientPermission("403")));
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
            .WithWebhookResult("owner/repo", Result<WebhookUpsertKind>.Fail(new Error("GitHub.ProviderError", "500")));
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
}
