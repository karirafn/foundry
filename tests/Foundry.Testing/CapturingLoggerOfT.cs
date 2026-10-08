using Microsoft.Extensions.Logging;

namespace Foundry.Testing;

/// <summary>
/// A typed capturing logger that wraps <see cref="CapturingLogger"/> so tests can inject an
/// <see cref="ILogger{TCategoryName}"/> and still assert on the captured entries.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly CapturingLogger _inner = new();

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries => _inner.Entries;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
