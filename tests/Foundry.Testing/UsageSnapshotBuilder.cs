using Foundry.Modules.Credentials.Features.Usage;

namespace Foundry.Testing;

/// <summary>
/// Test data builder for <see cref="UsageSnapshot"/>. Defaults to a valid snapshot with one
/// session rate-limit entry so tests can assert snapshot shape without constructing every field.
/// </summary>
public sealed class UsageSnapshotBuilder
{
    private IReadOnlyList<UsageRateLimit> _limits =
    [
        new UsageRateLimit(
            Kind: "session",
            Percent: 42m,
            ResetsAt: DateTimeOffset.UtcNow.AddHours(5),
            ModelDisplayName: null,
            Severity: "normal",
            IsActive: true),
    ];

    private UsageExtraCredits? _extraUsage;
    private DateTimeOffset _capturedAt = DateTimeOffset.UtcNow;
    private string _accountEmail = "user@example.com";
    private DateTimeOffset? _lastRefreshFailedAt;

    internal UsageSnapshotBuilder WithLimits(IReadOnlyList<UsageRateLimit> limits)
    {
        _limits = limits;
        return this;
    }

    internal UsageSnapshotBuilder WithLimit(UsageRateLimit limit)
    {
        _limits = [.._limits, limit];
        return this;
    }

    internal UsageSnapshotBuilder WithExtraUsage(UsageExtraCredits? extraUsage)
    {
        _extraUsage = extraUsage;
        return this;
    }

    public UsageSnapshotBuilder WithCapturedAt(DateTimeOffset capturedAt)
    {
        _capturedAt = capturedAt;
        return this;
    }

    public UsageSnapshotBuilder WithAccountEmail(string accountEmail)
    {
        _accountEmail = accountEmail;
        return this;
    }

    public UsageSnapshotBuilder WithLastRefreshFailedAt(DateTimeOffset? lastRefreshFailedAt)
    {
        _lastRefreshFailedAt = lastRefreshFailedAt;
        return this;
    }

    internal UsageSnapshot Build() =>
        new(
            Limits: _limits,
            ExtraUsage: _extraUsage,
            CapturedAt: _capturedAt,
            AccountEmail: _accountEmail,
            LastRefreshFailedAt: _lastRefreshFailedAt);
}
