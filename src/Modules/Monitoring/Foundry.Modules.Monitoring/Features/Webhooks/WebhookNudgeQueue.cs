using System.Threading.Channels;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// The bounded in-memory nudge channel (ADR 0085). Producers are the webhook endpoint and the
/// checks-pending deadline handler; the only consumer is <see cref="Polling.MonitoringService"/>.
/// A full channel drops the new nudge — the backstop poll recovers it. Nudges are not persisted:
/// a restart loses them, which the backstop also covers.
/// </summary>
internal sealed class WebhookNudgeQueue(ILogger<WebhookNudgeQueue> logger)
{
    internal const int Capacity = 1024;

    private readonly Channel<Nudge> _channel = Channel.CreateBounded<Nudge>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait,
    });

    internal ChannelReader<Nudge> Reader => _channel.Reader;

    /// <summary>
    /// Enqueues <paramref name="nudge"/> without waiting. Returns <see langword="false"/> (and logs at
    /// debug) when the channel is full and the nudge was dropped.
    /// </summary>
    public bool TryEnqueue(Nudge nudge)
    {
        if (_channel.Writer.TryWrite(nudge))
        {
            return true;
        }

        logger.LogDebug(
            "Nudge channel full; dropped {Pass} nudge for repository {RepositoryId} (issue {IssueNumber}). The backstop poll recovers it.",
            nudge.Key.Pass,
            nudge.Key.RepositoryId.Value,
            nudge.Key.IssueNumber);
        return false;
    }
}
