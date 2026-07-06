using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Credentials;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Login.LoginSessionServiceTests;

/// <summary>
/// Tests for finding #4: Unknown failure reasons must broadcast an opaque fixed message,
/// not the raw exception text. The raw detail is logged server-side only.
/// </summary>
public sealed class OpaqueErrorBroadcast
{
    private const string OAuthUrl = "https://claude.ai/oauth/authorize?code=true";
    private const string UrlLine = $"If the browser didn't open, visit: {OAuthUrl}";
    private const string OpaqueMessage = "An unexpected error occurred.";

    private static LoginSessionService CreateService(
        FakeCredentialsOrchestrator orchestrator,
        CapturingLoginSessionBroadcaster broadcaster,
        FakeLoginSuccessCommitter? committer = null)
    {
        return new LoginSessionService(
            orchestrator,
            committer ?? new FakeLoginSuccessCommitter(),
            broadcaster);
    }

    /// <summary>
    /// When auth-status fails with an unknown error after the container exits,
    /// the broadcast FailureMessage must be the opaque fixed string, not the raw error message.
    /// </summary>
    [Fact]
    public async Task WhenAuthStatusFails_FailureMessageIsOpaque()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new([UrlLine, "Login successful."]);
        orchestrator.WithExitedContainer(exitCode: 0);
        orchestrator.WithAuthStatusFailure(new Error("Auth.Failed", "internal-secret-error-detail"));

        CapturingLoginSessionBroadcaster broadcaster = new();
        LoginSessionService sut = CreateService(orchestrator, broadcaster);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.WaitForStartCompletedAsync();

        // Act
        await sut.SubmitCodeAsync("the-code", TestContext.Current.CancellationToken);

        // Assert — the broadcast must NOT contain the raw error string
        LoginSessionUpdate? failedUpdate = broadcaster.Captured
            .FirstOrDefault(u => u.Phase == LoginPhaseDiscriminator.Failed);
        failedUpdate.ShouldNotBeNull("session must reach Failed phase");
        string? message = failedUpdate!.FailureMessage;
        message.ShouldNotBeNull();
        message!.ShouldNotContain("internal-secret-error-detail");
        message.ShouldBe(OpaqueMessage);
    }
}
