using Docker.DotNet;
using Docker.DotNet.Models;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// Scriptable in-memory fake of <see cref="ISystemOperations"/> for unit-testing
/// <c>DockerDaemonHealthCheck</c> with zero Docker.
/// </summary>
internal sealed class FakeSystemOperations : ISystemOperations
{
    private Func<CancellationToken, Task>? _pingBehavior;

    /// <summary>Scripts <see cref="PingAsync"/> to complete successfully.</summary>
    public FakeSystemOperations WithSuccessfulPing()
    {
        _pingBehavior = _ => Task.CompletedTask;
        return this;
    }

    /// <summary>Scripts <see cref="PingAsync"/> to throw an <see cref="HttpRequestException"/>.</summary>
    public FakeSystemOperations WithFailedPing()
    {
        _pingBehavior = _ => Task.FromException(new HttpRequestException("Connection refused"));
        return this;
    }

    /// <summary>
    /// Scripts <see cref="PingAsync"/> to throw an <see cref="OperationCanceledException"/> immediately,
    /// simulating the linked timeout CTS firing before the real daemon responds.
    /// </summary>
    public FakeSystemOperations WithTimeoutPing()
    {
        _pingBehavior = ct => Task.FromException(new OperationCanceledException(ct));
        return this;
    }

    public Task PingAsync(CancellationToken cancellationToken)
    {
        if (_pingBehavior is null)
        {
            throw new InvalidOperationException(
                "No ping behavior scripted. Call WithSuccessfulPing, WithFailedPing, or WithTimeoutPing first.");
        }

        return _pingBehavior(cancellationToken);
    }

    public Task AuthenticateAsync(AuthConfig authConfig, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<VersionResponse> GetVersionAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<SystemInfoResponse> GetSystemInfoAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<Stream> MonitorEventsAsync(
        ContainerEventsParameters parameters,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task MonitorEventsAsync(
        ContainerEventsParameters parameters,
        IProgress<Message> progress,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
