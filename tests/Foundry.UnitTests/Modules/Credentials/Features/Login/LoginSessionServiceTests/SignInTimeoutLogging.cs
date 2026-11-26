using Foundry.Modules.Credentials.Features.Login;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Credentials;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Login.LoginSessionServiceTests;

public sealed class SignInTimeoutLogging
{
    private const string OAuthUrl = "https://claude.ai/oauth/authorize?code=true";
    private const string UrlLine = $"If the browser didn't open, visit: {OAuthUrl}";
    private const string SubmittedCode = "abcdefgh#ijklmnop";
    private const string CodePartBeforeHash = "abcdefgh";

    private static async Task<IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)>> RunTimedOutSignInAsync(
        IReadOnlyList<string> lines)
    {
        FakeCredentialsOrchestrator orchestrator = new(lines);
        orchestrator.WithBlockingStreamAfterLines();

        CapturingLogger<LoginSessionService> logger = new();
        LoginSessionService sut = new(
            orchestrator,
            new FakeLoginSuccessCommitter(),
            new CapturingLoginSessionBroadcaster(),
            logger);

        using CancellationTokenSource hostCts = new(TimeSpan.FromSeconds(30));
        await sut.StartAsync(hostCts.Token);
        await sut.WaitForStartCompletedAsync();

        Task<Result> submitTask = sut.SubmitCodeAsync(TestLoginCode.Of(SubmittedCode), hostCts.Token);

        // Give SubmitCodeAsync time to enter the sign-in scan after delivering the code.
        await Task.Delay(50, CancellationToken.None);
        sut.TriggerSignInTimeoutForTest();
        await submitTask;

        return logger.Entries;
    }

    [Fact]
    public async Task WhenSignInTimesOut_WarningMasksSubmittedCodeInLastLine()
    {
        // Arrange
        string[] lines = [UrlLine, $"Received code {SubmittedCode}, verifying {CodePartBeforeHash}"];

        // Act
        IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> entries =
            await RunTimedOutSignInAsync(lines);

        // Assert
        (LogLevel Level, string Message, Exception? Exception) warning =
            entries.Where(e => e.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldContain("***");
        warning.Message.ShouldNotContain(SubmittedCode);
        warning.Message.ShouldNotContain(CodePartBeforeHash);
    }

    [Fact]
    public async Task WhenSignInTimesOut_WarningNamesSessionAndLastLine()
    {
        // Arrange
        string[] lines = [UrlLine, "Still waiting for the server"];

        // Act
        IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> entries =
            await RunTimedOutSignInAsync(lines);

        // Assert
        (LogLevel Level, string Message, Exception? Exception) warning =
            entries.Where(e => e.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldContain("timed out after");
        warning.Message.ShouldContain("Last CLI line: Still waiting for the server");
    }

    [Fact]
    public async Task WhenSignInTimesOut_WarningCutsLastLineTo500Characters()
    {
        // Arrange
        string longLine = new('x', 600);
        string[] lines = [UrlLine, longLine];

        // Act
        IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> entries =
            await RunTimedOutSignInAsync(lines);

        // Assert
        (LogLevel Level, string Message, Exception? Exception) warning =
            entries.Where(e => e.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldEndWith("Last CLI line: " + new string('x', 500));
    }
}
