namespace Foundry.IntegrationTests.Smoke;

/// <summary>Polls for a positive condition instead of sleeping a fixed time, so the smoke test runs at production cadence.</summary>
internal static class Eventually
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        string description,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(condition);

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
            throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0} s waiting until {description}.");
        }
    }

    public static Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string description,
        CancellationToken cancellationToken)
        => WaitUntilAsync(() => Task.FromResult(condition()), timeout, description, cancellationToken);
}
