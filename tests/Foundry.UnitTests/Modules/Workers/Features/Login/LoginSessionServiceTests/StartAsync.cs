using Foundry.Modules.Workers.Features.Login;
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

        FakeWorkerOrchestrator orchestrator = new([logLine]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        LoginSession session = await sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        session.Phase.ShouldBeOfType<LoginPhase.WaitingForAuthorization>()
            .Url.ShouldBe(oauthUrl);
    }

    [Fact]
    public async Task WhenFakeEmitsOAuthUrl_IsLoginActiveIsTrue()
    {
        // Arrange
        string oauthUrl = "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";
        string logLine = $"If the browser didn't open, visit: {oauthUrl}";

        FakeWorkerOrchestrator orchestrator = new([logLine]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenNoUrlEmitted_TransitionsToFailed_WithUrlTimeout()
    {
        // Arrange
        FakeWorkerOrchestrator orchestrator = new([]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        LoginSession session = await sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        LoginPhase.Failed failed = session.Phase.ShouldBeOfType<LoginPhase.Failed>();
        failed.Reason.ShouldBeOfType<LoginFailureReason.UrlTimeout>();
    }

    [Fact]
    public async Task WhenNoUrlEmitted_IsLoginActiveBecomesFalse()
    {
        // Arrange
        FakeWorkerOrchestrator orchestrator = new([]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenCalledTwice_ReturnsSameSession()
    {
        // Arrange
        string oauthUrl = "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";
        string logLine = $"If the browser didn't open, visit: {oauthUrl}";

        FakeWorkerOrchestrator orchestrator = new([logLine]);
        LoginSessionService sut = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        LoginSession first = await sut.StartAsync(TestContext.Current.CancellationToken);
        LoginSession second = await sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        second.SessionId.ShouldBe(first.SessionId);
    }
}
