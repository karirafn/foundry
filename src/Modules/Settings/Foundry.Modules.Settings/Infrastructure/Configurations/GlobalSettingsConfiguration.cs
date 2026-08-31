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

        builder.Property(s => s.SystemPromptTemplate)
            .HasMaxLength(GlobalSettings.MaxPromptTemplateLength)
            .HasColumnName("system_prompt_template");

        builder.Property(s => s.WorkerPromptTemplate)
            .HasMaxLength(GlobalSettings.MaxPromptTemplateLength)
            .HasColumnName("worker_prompt_template");

        builder.Property(s => s.UsageLimitResetsAt)
            .HasColumnName("usage_limit_resets_at");

        builder.Property(s => s.IsDispatchPaused)
            .HasColumnName("is_dispatch_paused");

        builder.Property(s => s.AutoResumeOnUsageReset)
            .HasColumnName("auto_resume_on_usage_reset");

        ValueConverter<WorkerImageConfiguration, string> workerImageConfigConverter = new(
            config => SerializeWorkerImageConfiguration(config),
            json => DeserializeWorkerImageConfiguration(json));

        builder.Property(s => s.WorkerImageConfiguration)
            .HasConversion(workerImageConfigConverter)
            .HasColumnType("TEXT")
            .HasColumnName("worker_image_configuration");

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

    private static string SerializeAllowedProviderHosts(IReadOnlyList<string> hosts)
        => JsonSerializer.Serialize(hosts);

    private static List<string> DeserializeAllowedProviderHosts(string json)
        => JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static string SerializeWorkerImageConfiguration(WorkerImageConfiguration config)
        => JsonSerializer.Serialize(config);

    private static WorkerImageConfiguration DeserializeWorkerImageConfiguration(string json)
        => JsonSerializer.Deserialize<WorkerImageConfiguration>(json)
            ?? WorkerImageConfiguration.Default;

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
}
