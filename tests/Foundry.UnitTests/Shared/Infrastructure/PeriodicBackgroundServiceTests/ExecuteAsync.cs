using System.Threading;

using Foundry.Shared.Infrastructure;
using Foundry.Testing;

using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.PeriodicBackgroundServiceTests;

public sealed class ExecuteAsync
{
    [Fact]
    public async Task WhenTickThrowsNonCancellationException_ErrorIsLoggedAndLoopContinues()
    {
        // Arrange
        CapturingLogger logger = new();
        int tickCount = 0;
        InvalidOperationException tickException = new("boom");

        ThrowOnFirstTickService sut = new(logger, tickException, onTick: () => tickCount++);

        using CancellationTokenSource cts = new();

        // Act — start service, let first tick throw, wait for second tick, then stop
        await sut.StartAsync(cts.Token);
        await sut.SecondTickReached.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        // Assert
        tickCount.ShouldBeGreaterThanOrEqualTo(2, "loop must survive the first tick exception and keep ticking");
        logger.Entries.ShouldContain(
            e => e.Level == LogLevel.Error && e.Exception == tickException,
            "error from first tick must be logged at Error level");
    }

    [Fact]
    public async Task WhenStoppingTokenCancelled_LoopExitsWithNoErrorLogged()
    {
        // Arrange
        CapturingLogger logger = new();
        NeverThrowService sut = new(logger);

        using CancellationTokenSource cts = new();

        // Act — start, then cancel immediately
        await sut.StartAsync(cts.Token);
        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        // Assert
        logger.Entries.ShouldNotContain(
            e => e.Level == LogLevel.Error,
            "clean cancellation must not log any errors");
    }

    [Fact]
    public async Task WhenTickThrowsOperationCanceledAndStoppingTokenIsNotRequested_ErrorIsLogged()
    {
        // Arrange
        CapturingLogger logger = new();
        OperationCanceledException spuriousCancellation = new("cancelled from inside");
        SpuriousCancellationService sut = new(logger, spuriousCancellation);

        using CancellationTokenSource cts = new();

        // Act — start, let first tick throw OCE (without stopping token being requested), then stop
        await sut.StartAsync(cts.Token);
        await sut.FirstTickCompleted.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        // Assert
        logger.Entries.ShouldContain(
            e => e.Level == LogLevel.Error && e.Exception == spuriousCancellation,
            "OperationCanceledException when stopping token is not requested must be logged at Error");
    }

    // -------------------------------------------------------------------------
    // Test-only subclasses
    // -------------------------------------------------------------------------

    private sealed class ThrowOnFirstTickService : PeriodicBackgroundService
    {
        private readonly Exception _firstTickException;
        private readonly Action _onTick;
        private int _ticks;

        public SemaphoreSlim SecondTickReached { get; } = new(0, 1);

        public ThrowOnFirstTickService(ILogger logger, Exception firstTickException, Action onTick)
            : base(logger)
        {
            _firstTickException = firstTickException;
            _onTick = onTick;
        }

        protected override TimeSpan TickInterval => TimeSpan.FromMilliseconds(20);

        protected override Task TickAsync(CancellationToken cancellationToken)
        {
            int tick = Interlocked.Increment(ref _ticks);
            _onTick();

            if (tick == 1)
            {
                throw _firstTickException;
            }

            if (tick == 2)
            {
                SecondTickReached.Release();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class NeverThrowService : PeriodicBackgroundService
    {
        public NeverThrowService(ILogger logger) : base(logger) { }

        protected override TimeSpan TickInterval => TimeSpan.FromMilliseconds(20);

        protected override Task TickAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SpuriousCancellationService : PeriodicBackgroundService
    {
        private readonly OperationCanceledException _exception;
        private int _ticks;

        public SemaphoreSlim FirstTickCompleted { get; } = new(0, 1);

        public SpuriousCancellationService(ILogger logger, OperationCanceledException exception)
            : base(logger)
        {
            _exception = exception;
        }

        protected override TimeSpan TickInterval => TimeSpan.FromMilliseconds(20);

        protected override Task TickAsync(CancellationToken cancellationToken)
        {
            int tick = Interlocked.Increment(ref _ticks);

            if (tick == 1)
            {
                FirstTickCompleted.Release();
                // Throw OCE with a fresh token that is NOT the stopping token
                throw _exception;
            }

            return Task.CompletedTask;
        }
    }
}
