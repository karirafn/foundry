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
        LoginSessionService sut = new(orchestrator);

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
        LoginSessionService sut = new(orchestrator);

        // Act
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        ((ILoginSessionState)sut).IsLoginActive.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenNoUrlEmitted_SessionIsInStartingPhase()
    {
        // Arrange
        FakeWorkerOrchestrator orchestrator = new([]);
        LoginSessionService sut = new(orchestrator);

        // Act
        LoginSession session = await sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        session.Phase.ShouldBeOfType<LoginPhase.Starting>();
    }

    [Fact]
    public async Task WhenCalledTwice_ReturnsSameSession()
    {
        // Arrange
        string oauthUrl = "https://claude.com/cai/oauth/authorize?code=true&client_id=abc&state=xyz";
        string logLine = $"If the browser didn't open, visit: {oauthUrl}";

        FakeWorkerOrchestrator orchestrator = new([logLine]);
        LoginSessionService sut = new(orchestrator);

        // Act
        LoginSession first = await sut.StartAsync(TestContext.Current.CancellationToken);
        LoginSession second = await sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        second.SessionId.ShouldBe(first.SessionId);
    }
}
