using Foundry.Modules.Credentials.Features.Login;
using Foundry.UnitTests.Fakes.Credentials;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Login.LoginSessionServiceTests;

public sealed class StartAsync
{
    [Fact]
    public async Task WhenFakeEmitsOAuthUrl_TransitionsToWaitingForAuthorization()
    {
        // Arrange
        string oauthUrl = "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";
        string logLine = $"If the browser didn't open, visit: {oauthUrl}";

        FakeCredentialsOrchestrator orchestrator = new([logLine]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Assert
        sut.ActiveSessionPhaseForTest
            .ShouldBeOfType<LoginPhase.WaitingForAuthorization>()
            .Url.ShouldBe(oauthUrl);
    }

    [Fact]
    public async Task WhenFakeEmitsOAuthUrl_IsLoginActiveIsTrue()
    {
        // Arrange
        string oauthUrl = "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";
        string logLine = $"If the browser didn't open, visit: {oauthUrl}";

        FakeCredentialsOrchestrator orchestrator = new([logLine]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Assert
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenNoUrlEmitted_TransitionsToFailed_WithUrlTimeout()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new([]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Assert
        sut.ActiveSessionPhaseForTest.ShouldBeNull();
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenNoUrlEmitted_IsLoginActiveBecomesFalse()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new([]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Assert
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenCalledTwice_ReturnsSameSessionId()
    {
        // Arrange
        string oauthUrl = "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";
        string logLine = $"If the browser didn't open, visit: {oauthUrl}";

        FakeCredentialsOrchestrator orchestrator = new([logLine]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        Guid first = await sut.StartAsync(TestContext.Current.CancellationToken);
        Guid second = await sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        second.ShouldBe(first);
    }

    [Fact]
    public async Task StartAsync_ReturnsNonEmptySessionId()
    {
        // Arrange
        string oauthUrl = "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";
        string logLine = $"If the browser didn't open, visit: {oauthUrl}";

        FakeCredentialsOrchestrator orchestrator = new([logLine]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        Guid sessionId = await sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        sessionId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task StartAsync_ReturnsImmediately_BeforeUrlScanCompletes()
    {
        // Arrange — orchestrator with a URL so background task has work to do
        string oauthUrl = "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";
        string logLine = $"If the browser didn't open, visit: {oauthUrl}";

        FakeCredentialsOrchestrator orchestrator = new([logLine]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act — StartAsync should return without waiting for the URL scan
        Guid sessionId = await sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert — we have a session id immediately, before awaiting the background task
        sessionId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task WhenLogLineExceedsLineCap_LineIsSkipped_NoUrlExtracted()
    {
        // Arrange — the URL is embedded in a line that exceeds the 8 KB cap; it must be skipped
        string oauthUrl = "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";
        string padding = new('x', 8_193);
        string oversizedLine = $"{padding} visit: {oauthUrl}";

        FakeCredentialsOrchestrator orchestrator = new([oversizedLine]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act — background task should exhaust the log stream without finding a URL
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Assert — session failed with UrlTimeout (line was skipped, URL not extracted)
        sut.ActiveSessionPhaseForTest.ShouldBeNull();
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
    }
}
