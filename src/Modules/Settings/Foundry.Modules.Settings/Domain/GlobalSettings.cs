using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain;

public sealed class GlobalSettings : AggregateRoot<GlobalSettingsId>
{
    private const int MinMaxConcurrent = 1;
    private const int MaxMaxConcurrent = 20;
    private const int MinTimeoutMinutes = 1;
    private const int MaxTimeoutMinutes = 1440;
    private const int DefaultMaxConcurrent = 3;
    private const int DefaultTimeoutMinutes = 120;

    // Private parameterless constructor for EF Core materialization.
    private GlobalSettings() : base(GlobalSettingsId.Default)
    {
    }

    private GlobalSettings(GlobalSettingsId id, DateTimeOffset createdAt) : base(id)
    {
        AuthMode = new AuthMode.ApiKey(string.Empty);
        MaxConcurrent = DefaultMaxConcurrent;
        TimeoutMinutes = DefaultTimeoutMinutes;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public AuthMode AuthMode { get; private set; } = null!;

    public int MaxConcurrent { get; private set; }

    public int TimeoutMinutes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static GlobalSettings Create()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new GlobalSettings(GlobalSettingsId.Default, now);
    }

    public void SetAuthMode(AuthMode mode)
    {
        AuthMode = mode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Result UpdateLimits(int maxConcurrent, int timeoutMinutes)
    {
        if (maxConcurrent < MinMaxConcurrent || maxConcurrent > MaxMaxConcurrent)
        {
            return SettingsErrors.InvalidMaxConcurrent(maxConcurrent);
        }

        if (timeoutMinutes < MinTimeoutMinutes || timeoutMinutes > MaxTimeoutMinutes)
        {
            return SettingsErrors.InvalidTimeout(timeoutMinutes);
        }

        MaxConcurrent = maxConcurrent;
        TimeoutMinutes = timeoutMinutes;
        UpdatedAt = DateTimeOffset.UtcNow;
        return Result.Ok();
    }
}
