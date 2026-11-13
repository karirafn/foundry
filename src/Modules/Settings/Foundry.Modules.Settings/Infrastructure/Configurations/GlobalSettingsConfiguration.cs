using System.Text.Json;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Settings.Infrastructure.Configurations;

internal sealed class GlobalSettingsConfiguration : IEntityTypeConfiguration<GlobalSettings>
{
    private static readonly JsonSerializerOptions ImageBuildStateOptions = BuildImageBuildStateOptions();

    public void Configure(EntityTypeBuilder<GlobalSettings> builder)
    {
        builder.ToTable("global_settings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasConversion(new StronglyTypedIdValueConverter<GlobalSettingsId>())
            .HasColumnName("id");

        builder.Property(s => s.MaxConcurrent)
            .HasColumnName("max_concurrent");

        builder.Property(s => s.TimeoutMinutes)
            .HasColumnName("timeout_minutes");

        builder.Property(s => s.ProbeIntervalMinutes)
            .HasColumnName("probe_interval_minutes");

        builder.Property(s => s.PollIntervalSeconds)
            .HasColumnName("poll_interval_seconds")
            .HasDefaultValue(GlobalSettings.DefaultPollIntervalSeconds);

        builder.Property(s => s.BackstopIntervalMinutes)
            .HasColumnName("backstop_interval_minutes")
            .HasDefaultValue(GlobalSettings.DefaultBackstopIntervalMinutes);

        builder.Property(s => s.WebhookPublicBaseUrl)
            .HasMaxLength(GlobalSettings.MaxWebhookPublicBaseUrlLength)
            .HasColumnName("webhook_public_base_url");

        builder.Property(s => s.UsageLimitResetsAt)
            .HasColumnName("usage_limit_resets_at");

        builder.Property(s => s.IsDispatchPaused)
            .HasColumnName("is_dispatch_paused");

        builder.Property(s => s.AutoResumeOnUsageReset)
            .HasColumnName("auto_resume_on_usage_reset");

        ValueConverter<IReadOnlyList<HostPause>, string> hostPausesConverter = new(
            pauses => SerializeHostPauses(pauses),
            json => DeserializeHostPauses(json));

        ValueComparer<IReadOnlyList<HostPause>> hostPausesComparer = new(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            pauses => pauses.Aggregate(0, (hash, p) => HashCode.Combine(hash, p.GetHashCode())),
            pauses => pauses.ToList());

        // One JSON TEXT column: a handful of hosts at most, always read and written with the rest of the settings.
        builder.Property(s => s.HostPauses)
            .HasConversion(hostPausesConverter, hostPausesComparer)
            .HasColumnType("TEXT")
            .HasColumnName("host_pauses");

        ValueConverter<WorkerImageConfiguration, string> workerImageConfigConverter = new(
            config => SerializeWorkerImageConfiguration(config),
            json => DeserializeWorkerImageConfiguration(json));

        // WorkerImageConfiguration is serialized as a single JSON TEXT column. HasMaxLength is
        // inapplicable here — it governs individual varchar/nvarchar column widths, not a JSON blob.
        // The image tag is capped at 255 characters (the Docker reference limit) by domain logic
        // in WorkerImageConfiguration.Create, enforced via the MaxImageTagLength constant.
        builder.Property(s => s.WorkerImageConfiguration)
            .HasConversion(workerImageConfigConverter)
            .HasColumnType("TEXT")
            .HasColumnName("worker_image_configuration");

        ValueConverter<WorkerResourceLimits, string> workerResourceLimitsConverter = new(
            limits => SerializeWorkerResourceLimits(limits),
            json => DeserializeWorkerResourceLimits(json));

        // Serialized as a single JSON TEXT column; ranges are enforced by WorkerResourceLimits.Create.
        builder.Property(s => s.WorkerResourceLimits)
            .HasConversion(workerResourceLimitsConverter)
            .HasColumnType("TEXT")
            .HasColumnName("worker_resource_limits");

        ValueConverter<ClaudeCodeSettings, string> claudeCodeSettingsConverter = new(
            settings => SerializeClaudeCodeSettings(settings),
            json => DeserializeClaudeCodeSettings(json));

        ValueComparer<ClaudeCodeSettings> claudeCodeSettingsComparer = new(
            (a, b) => a != null && b != null && a.Equals(b),
            settings => settings.GetHashCode(),
            settings => settings);

        // Serialized as a single JSON TEXT column; the model length and deny-rule format are enforced
        // by ClaudeCodeSettings.Create. The record is immutable, so the comparer snapshots by reference.
        builder.Property(s => s.ClaudeCodeSettings)
            .HasConversion(claudeCodeSettingsConverter, claudeCodeSettingsComparer)
            .HasColumnType("TEXT")
            .HasColumnName("claude_code_settings");

        ValueConverter<IReadOnlyList<string>, string> allowedProviderHostsConverter = new(
            hosts => SerializeAllowedProviderHosts(hosts),
            json => DeserializeAllowedProviderHosts(json));

        ValueComparer<IReadOnlyList<string>> allowedProviderHostsComparer = new(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            hosts => hosts.Aggregate(0, (hash, h) => HashCode.Combine(hash, h.GetHashCode(StringComparison.Ordinal))),
            hosts => hosts.ToList());

        // AllowedProviderHosts is serialized as a single JSON TEXT column. HasMaxLength is
        // inapplicable here — it governs individual varchar/nvarchar column widths, not a
        // JSON blob. Entry-length (255 chars) and list-count (50 entries) caps are enforced
        // by domain logic in GlobalSettings.UpdateAllowedProviderHosts.
        builder.Property(s => s.AllowedProviderHosts)
            .HasConversion(allowedProviderHostsConverter, allowedProviderHostsComparer)
            .HasColumnType("TEXT")
            .HasColumnName("allowed_provider_hosts");

        ValueConverter<ImageBuildState, string> imageBuildStateConverter = new(
            state => SerializeImageBuildState(state),
            json => DeserializeImageBuildState(json));

        builder.Property(s => s.ImageBuildState)
            .HasConversion(imageBuildStateConverter)
            .HasColumnType("TEXT")
            .HasColumnName("image_build_status");

        builder.Property(s => s.LastImageBuiltAt)
            .HasColumnName("last_image_built_at");

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at");
    }

    private static string SerializeHostPauses(IReadOnlyList<HostPause> pauses)
        => JsonSerializer.Serialize(pauses.Select(p => new StoredHostPause(p.Host, p.PausedAt, p.ProbeAttempts, p.NextProbeAt)));

    private static List<HostPause> DeserializeHostPauses(string json)
        => (JsonSerializer.Deserialize<List<StoredHostPause>>(json) ?? [])
            .Select(p => HostPause.Restore(p.Host, p.PausedAt, p.ProbeAttempts, p.NextProbeAt))
            .ToList();

    private static string SerializeAllowedProviderHosts(IReadOnlyList<string> hosts)
        => JsonSerializer.Serialize(hosts);

    private static List<string> DeserializeAllowedProviderHosts(string json)
        => JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static string SerializeWorkerImageConfiguration(WorkerImageConfiguration config)
        => JsonSerializer.Serialize(config);

    private static WorkerImageConfiguration DeserializeWorkerImageConfiguration(string json)
    {
        WorkerImageConfiguration? config = JsonSerializer.Deserialize<WorkerImageConfiguration>(json);

        if (config is null)
        {
            return WorkerImageConfiguration.Default;
        }

        // Rows written before the ImageTag/BuildEnabled fields were added have a null/empty ImageTag.
        // Patch in the Default values for those two fields while preserving the existing install flags,
        // so existing rows load correctly until the AddWorkerImageTagAndBuildToggle backfill migration runs.
        if (string.IsNullOrWhiteSpace(config.ImageTag))
        {
            return WorkerImageConfiguration.Create(
                    imageTag: WorkerImageConfiguration.Default.ImageTag,
                    buildEnabled: WorkerImageConfiguration.Default.BuildEnabled,
                    installDotnet: config.InstallDotnet,
                    installAngular: config.InstallAngular,
                    installChromium: config.InstallChromium,
                    installDocker: config.InstallDocker)
                .Match(
                    onSuccess: v => v,
                    onFailure: e => WorkerImageConfiguration.Default);
        }

        return config;
    }

    private static string SerializeWorkerResourceLimits(WorkerResourceLimits limits)
        => JsonSerializer.Serialize(limits);

    private static WorkerResourceLimits DeserializeWorkerResourceLimits(string json)
        => JsonSerializer.Deserialize<WorkerResourceLimits>(json)
            ?? WorkerResourceLimits.Default;

    private static string SerializeClaudeCodeSettings(ClaudeCodeSettings settings)
        => JsonSerializer.Serialize(settings);

    private static ClaudeCodeSettings DeserializeClaudeCodeSettings(string json)
        => JsonSerializer.Deserialize<ClaudeCodeSettings>(json)
            ?? ClaudeCodeSettings.Default;

    private static JsonSerializerOptions BuildImageBuildStateOptions()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new ImageBuildStateJsonConverter());
        return options;
    }

    private static string SerializeImageBuildState(ImageBuildState state)
        => JsonSerializer.Serialize(state, ImageBuildStateOptions);

    private static ImageBuildState DeserializeImageBuildState(string json)
        => JsonSerializer.Deserialize<ImageBuildState>(json, ImageBuildStateOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize ImageBuildState from JSON: {json}");

    // The stored shape, kept apart from HostPause so the column format never follows a domain refactor.
    private sealed record StoredHostPause(string Host, DateTimeOffset PausedAt, int ProbeAttempts, DateTimeOffset NextProbeAt);
}
