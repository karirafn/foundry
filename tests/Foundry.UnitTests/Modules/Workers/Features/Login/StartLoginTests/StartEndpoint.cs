using Foundry.Modules.Credentials.Features.Login;
using Foundry.UnitTests.Fakes.Credentials;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.AspNetCore.Http.HttpResults;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Login.StartLoginTests;

public sealed class StartEndpoint
{
    [Fact]
    public async Task WhenNoActiveSession_Returns202WithSessionId()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";
        FakeCredentialsOrchestrator orchestrator = new([logLine]);
        LoginSessionService service = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        Accepted<StartLogin.Response> result = await StartLogin.Endpoint.HandleAsync(
            service,
            TestContext.Current.CancellationToken);

        // Assert
        result.StatusCode.ShouldBe(202);
        result.Value.ShouldNotBeNull();
        result.Value.SessionId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task WhenActiveSessionExists_ReturnsExistingSessionId()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";
        FakeCredentialsOrchestrator orchestrator = new([logLine]);
        LoginSessionService service = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        Accepted<StartLogin.Response> first = await StartLogin.Endpoint.HandleAsync(
            service,
            TestContext.Current.CancellationToken);
        Guid firstSessionId = first.Value!.SessionId;

        // Act — second call returns the same session
        Accepted<StartLogin.Response> second = await StartLogin.Endpoint.HandleAsync(
            service,
            TestContext.Current.CancellationToken);

        // Assert
        second.Value.ShouldNotBeNull();
        second.Value.SessionId.ShouldBe(firstSessionId);
    }
}
