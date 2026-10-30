namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// No-op fallback implementation of <see cref="IUsageSnapshotBroadcaster"/> registered by the
/// Credentials module so the module's DI graph is self-contained and scope validation passes
/// before the WebApi host replaces this with the real SignalR implementation.
/// <para>
/// The WebApi host registers <c>SignalRUsageSnapshotBroadcaster</c> via a plain
/// <c>AddSingleton</c> call after <c>AddCredentialsModule</c> — since .NET DI resolves the
/// <em>last</em> registration for a given service type, the real implementation wins in
/// production. This no-op is only ever resolved in isolated module tests or when the WebApi
/// host has not yet registered its replacement.
/// </para>
/// </summary>
internal sealed class NoOpUsageSnapshotBroadcaster : IUsageSnapshotBroadcaster
{
    public Task SendSnapshotAsync(UsageSnapshot? snapshot, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
