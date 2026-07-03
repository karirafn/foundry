using Foundry.Modules.Workers.Features.Login;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.AspNetCore.Http.HttpResults;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Login.SubmitLoginCodeTests;

public sealed class SubmitEndpoint
{
    [Fact]
    public async Task WhenCodeIsEmpty_Returns400()
    {
        // Arrange
        FakeWorkerOrchestrator orchestrator = new([]);
        LoginSessionService service = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        Results<Ok, BadRequest<string>, UnprocessableEntity<string>> result =
            await SubmitLoginCode.Endpoint.HandleAsync(
                new SubmitLoginCode.Request(Code: string.Empty),
                service,
                TestContext.Current.CancellationToken);

        // Assert
        result.Result.ShouldBeOfType<BadRequest<string>>();
    }

    [Fact]
    public async Task WhenCodeIsWhitespace_Returns400()
    {
        // Arrange
        FakeWorkerOrchestrator orchestrator = new([]);
        LoginSessionService service = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        Results<Ok, BadRequest<string>, UnprocessableEntity<string>> result =
            await SubmitLoginCode.Endpoint.HandleAsync(
                new SubmitLoginCode.Request(Code: "   "),
                service,
                TestContext.Current.CancellationToken);

        // Assert
        result.Result.ShouldBeOfType<BadRequest<string>>();
    }

    [Fact]
    public async Task WhenNoActiveSession_Returns422()
    {
        // Arrange
        FakeWorkerOrchestrator orchestrator = new([]);
        LoginSessionService service = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act — no session started; code is non-empty
        Results<Ok, BadRequest<string>, UnprocessableEntity<string>> result =
            await SubmitLoginCode.Endpoint.HandleAsync(
                new SubmitLoginCode.Request(Code: "valid-code"),
                service,
                TestContext.Current.CancellationToken);

        // Assert
        result.Result.ShouldBeOfType<UnprocessableEntity<string>>();
    }

    [Fact]
    public async Task WhenSessionAcceptsCode_Returns200()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";
        FakeWorkerOrchestrator orchestrator = new([logLine, "Login successful."]);
        orchestrator.WithExitedContainer(exitCode: 0);
        LoginSessionService service = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Start session and wait for it to reach WaitingForAuthorization
        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.WaitForStartCompletedAsync();

        // Act
        Results<Ok, BadRequest<string>, UnprocessableEntity<string>> result =
            await SubmitLoginCode.Endpoint.HandleAsync(
                new SubmitLoginCode.Request(Code: "abc123"),
                service,
                TestContext.Current.CancellationToken);

        // Assert
        result.Result.ShouldBeOfType<Ok>();
    }
}
