namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// Lets one Package Source probe pass run at a time across the host, so a probe started by a credential change waits
/// for a scheduled pass that is already running and records its verdict after it.
/// </summary>
internal sealed class PackageSourceProbeGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    public void Dispose() => _semaphore.Dispose();

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
