using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.UnitTests.Fakes.Credentials;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Login.SubmitLoginCodeTests;

public sealed class SubmitEndpoint
{
    private const string LoginUrlLine = "If the browser didn't open, visit: https://claude.ai/oauth/authorize?code=true";

    [Fact]
    public async Task WhenCodeIsEmpty_Returns400WithLoginCodeEmptyCode()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new([]);
        LoginSessionService service = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        Results<Ok, ProblemHttpResult> result =
            await SubmitLoginCode.Endpoint.HandleAsync(
                new SubmitLoginCode.Request(Code: string.Empty),
                service,
                TestContext.Current.CancellationToken);

        // Assert
        ProblemHttpResult problem = result.Result.ShouldBeOfType<ProblemHttpResult>();
        problem.ShouldSatisfyAllConditions(
            () => problem.StatusCode.ShouldBe(StatusCodes.Status400BadRequest),
            () => problem.ProblemDetails.Type.ShouldEndWith(CredentialsErrors.LoginCodeEmptyCode),
            () => problem.ProblemDetails.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenCodeIsWhitespace_Returns400WithLoginCodeEmptyCode()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new([]);
        LoginSessionService service = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act
        Results<Ok, ProblemHttpResult> result =
            await SubmitLoginCode.Endpoint.HandleAsync(
                new SubmitLoginCode.Request(Code: "   "),
                service,
                TestContext.Current.CancellationToken);

        // Assert
        ProblemHttpResult problem = result.Result.ShouldBeOfType<ProblemHttpResult>();
        problem.ShouldSatisfyAllConditions(
            () => problem.StatusCode.ShouldBe(StatusCodes.Status400BadRequest),
            () => problem.ProblemDetails.Type.ShouldEndWith(CredentialsErrors.LoginCodeEmptyCode),
            () => problem.ProblemDetails.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenCodeExceedsMaxLength_Returns400WithLoginCodeTooLongCode()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new([]);
        LoginSessionService service = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);
        string overLongCode = new string('x', 513);

        // Act
        Results<Ok, ProblemHttpResult> result =
            await SubmitLoginCode.Endpoint.HandleAsync(
                new SubmitLoginCode.Request(Code: overLongCode),
                service,
                TestContext.Current.CancellationToken);

        // Assert
        ProblemHttpResult problem = result.Result.ShouldBeOfType<ProblemHttpResult>();
        problem.ShouldSatisfyAllConditions(
            () => problem.StatusCode.ShouldBe(StatusCodes.Status400BadRequest),
            () => problem.ProblemDetails.Type.ShouldEndWith(CredentialsErrors.LoginCodeTooLongCode),
            () => problem.ProblemDetails.Detail.ShouldNotBeNullOrEmpty());
    }

    [Theory]
    [InlineData("INVALID-CODE-FOR-TEST", CredentialsErrors.LoginCodeIncompleteCode, "That doesn't look like the full code. Copy all of it, including the # in the middle.")]
    [InlineData("https://claude.ai/magic-link#abc", CredentialsErrors.LoginCodeIsWebAddressCode, "That's a web address, not the code. Paste the code the last sign-in page shows.")]
    [InlineData("abc#def\nxyz", CredentialsErrors.LoginCodeHasControlCharactersCode, "The code contains line breaks or control characters. Copy just the code and paste it again.")]
    public async Task WhenCodeIsMalformedWhileWaitingForAuthorization_Returns400AndDeliversNothing(
        string code,
        string expectedErrorCode,
        string expectedDetail)
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new([LoginUrlLine]);
        orchestrator.WithBlockingStreamAfterLines();
        LoginSessionService service = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);
        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.WaitForStartCompletedAsync();

        // Act
        Results<Ok, ProblemHttpResult> result =
            await SubmitLoginCode.Endpoint.HandleAsync(
                new SubmitLoginCode.Request(Code: code),
                service,
                TestContext.Current.CancellationToken);

        // Assert
        ProblemHttpResult problem = result.Result.ShouldBeOfType<ProblemHttpResult>();
        problem.ShouldSatisfyAllConditions(
            () => problem.StatusCode.ShouldBe(StatusCodes.Status400BadRequest),
            () => problem.ProblemDetails.Type.ShouldEndWith(expectedErrorCode),
            () => problem.ProblemDetails.Detail.ShouldBe(expectedDetail),
            () => service.ActiveSessionPhaseForTest.ShouldBeOfType<LoginPhase.WaitingForAuthorization>(),
            () => orchestrator.DeliverLoginCodeCallCount.ShouldBe(0));
    }

    [Fact]
    public async Task WhenCodeIsRealisticOAuthLength_DoesNotReturn400()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new([]);
        LoginSessionService service = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);
        string realisticCode = new string('x', 64) + "#" + new string('y', 64);

        // Act
        Results<Ok, ProblemHttpResult> result =
            await SubmitLoginCode.Endpoint.HandleAsync(
                new SubmitLoginCode.Request(Code: realisticCode),
                service,
                TestContext.Current.CancellationToken);

        // Assert
        result.Result.ShouldBeOfType<ProblemHttpResult>()
            .StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task WhenNoActiveSession_Returns422()
    {
        // Arrange
        FakeCredentialsOrchestrator orchestrator = new([]);
        LoginSessionService service = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Act — no session started; code is non-empty
        Results<Ok, ProblemHttpResult> result =
            await SubmitLoginCode.Endpoint.HandleAsync(
                new SubmitLoginCode.Request(Code: "valid-code#state"),
                service,
                TestContext.Current.CancellationToken);

        // Assert
        ProblemHttpResult problem = result.Result.ShouldBeOfType<ProblemHttpResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task WhenSessionAcceptsCode_Returns200()
    {
        // Arrange
        string url = "https://claude.ai/oauth/authorize?code=true";
        string logLine = $"If the browser didn't open, visit: {url}";
        FakeCredentialsOrchestrator orchestrator = new([logLine, "Login successful."]);
        orchestrator.WithExitedContainer(exitCode: 0);
        LoginSessionService service = new(orchestrator, new FakeLoginSuccessCommitter(), NullLoginSessionBroadcaster.Instance);

        // Start session and wait for it to reach WaitingForAuthorization
        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.WaitForStartCompletedAsync();

        // Act
        Results<Ok, ProblemHttpResult> result =
            await SubmitLoginCode.Endpoint.HandleAsync(
                new SubmitLoginCode.Request(Code: "abc#123"),
                service,
                TestContext.Current.CancellationToken);

        // Assert
        result.Result.ShouldBeOfType<Ok>();
    }
}
