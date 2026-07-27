namespace Foundry.Modules.Workers.Features.Orchestration;

/// <summary>
/// Shared predicate for classifying Docker daemon connectivity failures.
/// A failure is considered a connectivity failure when it is an <see cref="HttpRequestException"/>,
/// a <see cref="TimeoutException"/>, or a self-issued <see cref="OperationCanceledException"/>
/// (i.e. the CancellationToken supplied by the caller was NOT already cancelled — so the timeout
/// CTS inside the caller fired, not cooperative shutdown).
/// Any other <c>DockerApiException</c> propagates as an unexpected error.
/// </summary>
internal static class DockerDaemonConnectivity
{
    public static bool IsUnreachable(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || exception is TimeoutException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}
