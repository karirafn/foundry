namespace Foundry.IntegrationTests.Smoke;

/// <summary>Polls for a positive condition instead of sleeping a fixed time, so the smoke test runs at production cadence.</summary>
internal static class Eventually
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Waits until <paramref name="condition"/> holds. On timeout the exception carries
    /// <paramref name="describeState"/>'s snapshot, since a composed host that stalls leaves no stack trace.
    /// </summary>
    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        string description,
        Func<Task<string>> describeState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(describeState);

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            while (!await condition())
            {
                await Task.Delay(PollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out after {timeout.TotalSeconds:0} s waiting until {description}.{Environment.NewLine}{await describeState()}");
        }
    }
}
