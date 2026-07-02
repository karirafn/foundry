using System.Text.Json;

using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Settings.Infrastructure.Configurations;

internal sealed class GlobalSettingsConfiguration(
    IDataProtectionProvider dataProtectionProvider,
    ILogger<EncryptedStringConverter>? encryptedStringConverterLogger = null)
    : IEntityTypeConfiguration<GlobalSettings>
{
    private static readonly JsonSerializerOptions SerializerOptions = BuildSerializerOptions();
    private static readonly JsonSerializerOptions ImageBuildStateOptions = BuildImageBuildStateOptions();

    public void Configure(EntityTypeBuilder<GlobalSettings> builder)
    {
        builder.ToTable("global_settings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasConversion(new StronglyTypedIdValueConverter<GlobalSettingsId>())
            .HasColumnName("id");

        EncryptedStringConverter encryptedConverter = new(dataProtectionProvider, encryptedStringConverterLogger);
        Func<string, string> encrypt = encryptedConverter.ConvertToProviderExpression.Compile();
        Func<string, string> decrypt = encryptedConverter.ConvertFromProviderExpression.Compile();

        ValueConverter<AuthMode, string> authModeConverter = new(
            mode => encrypt(SerializeAuthMode(mode)),
            encrypted => DeserializeAuthMode(decrypt(encrypted)));

        builder.Property(s => s.AuthMode)
            .HasConversion(authModeConverter)
            .HasColumnType("TEXT")
            .HasColumnName("auth_mode");

        builder.Property(s => s.MaxConcurrent)
            .HasColumnName("max_concurrent");

        builder.Property(s => s.TimeoutMinutes)
            .HasColumnName("timeout_minutes");

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

        builder.Property(s => s.DefaultCooldownMinutes)
            .HasColumnName("default_cooldown_minutes");

        builder.Property(s => s.AuthInvalidPause)
            .HasColumnName("auth_invalid_pause");

        ValueConverter<WorkerImageConfiguration, string> workerImageConfigConverter = new(
            config => SerializeWorkerImageConfiguration(config),
            json => DeserializeWorkerImageConfiguration(json));

        builder.Property(s => s.WorkerImageConfiguration)
            .HasConversion(workerImageConfigConverter)
            .HasColumnType("TEXT")
            .HasColumnName("worker_image_configuration");

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

    private static string SerializeWorkerImageConfiguration(WorkerImageConfiguration config)
        => JsonSerializer.Serialize(config);

    private static WorkerImageConfiguration DeserializeWorkerImageConfiguration(string json)
        => JsonSerializer.Deserialize<WorkerImageConfiguration>(json)
            ?? WorkerImageConfiguration.Default;

    private static JsonSerializerOptions BuildSerializerOptions()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new AuthModeJsonConverter());
        return options;
    }

    private static JsonSerializerOptions BuildImageBuildStateOptions()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new ImageBuildStateJsonConverter());
        return options;
    }

    private static string SerializeAuthMode(AuthMode mode)
        => JsonSerializer.Serialize(mode, SerializerOptions);

    private static AuthMode DeserializeAuthMode(string json)
        => JsonSerializer.Deserialize<AuthMode>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize AuthMode from JSON: {json}");

    private static string SerializeImageBuildState(ImageBuildState state)
        => JsonSerializer.Serialize(state, ImageBuildStateOptions);

    private static ImageBuildState DeserializeImageBuildState(string json)
        => JsonSerializer.Deserialize<ImageBuildState>(json, ImageBuildStateOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize ImageBuildState from JSON: {json}");
}
