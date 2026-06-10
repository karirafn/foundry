using System.Text.Json;

using Foundry.Modules.Settings.Domain;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Settings.Infrastructure.Configurations;

public sealed class GlobalSettingsConfiguration(IDataProtectionProvider dataProtectionProvider)
    : IEntityTypeConfiguration<GlobalSettings>
{
    private static readonly JsonSerializerOptions SerializerOptions = BuildSerializerOptions();

    public void Configure(EntityTypeBuilder<GlobalSettings> builder)
    {
        builder.ToTable("global_settings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasConversion(new StronglyTypedIdValueConverter<GlobalSettingsId>())
            .HasColumnName("id");

        EncryptedStringConverter encryptedConverter = new(dataProtectionProvider);
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

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at");
    }

    private static JsonSerializerOptions BuildSerializerOptions()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new AuthModeJsonConverter());
        return options;
    }

    private static string SerializeAuthMode(AuthMode mode)
        => JsonSerializer.Serialize(mode, SerializerOptions);

    private static AuthMode DeserializeAuthMode(string json)
        => JsonSerializer.Deserialize<AuthMode>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize AuthMode from JSON: {json}");
}
