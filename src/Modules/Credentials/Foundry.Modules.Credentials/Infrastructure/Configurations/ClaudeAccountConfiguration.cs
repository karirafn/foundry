using System.Text.Json;

using Foundry.Modules.Credentials.Domain;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Credentials.Infrastructure.Configurations;

internal sealed class ClaudeAccountConfiguration(
    IDataProtectionProvider dataProtectionProvider,
    ILogger<EncryptedStringConverter>? encryptedStringConverterLogger = null)
    : IEntityTypeConfiguration<ClaudeAccount>
{
    private static readonly JsonSerializerOptions AuthModeSerializerOptions = BuildAuthModeSerializerOptions();
    private static readonly JsonSerializerOptions ValiditySerializerOptions = BuildValiditySerializerOptions();

    public void Configure(EntityTypeBuilder<ClaudeAccount> builder)
    {
        builder.ToTable("claude_account");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasConversion(new StronglyTypedIdValueConverter<ClaudeAccountId>())
            .HasColumnName("id");

        EncryptedStringConverter encryptedConverter = new(dataProtectionProvider, encryptedStringConverterLogger);
        Func<string, string> encrypt = encryptedConverter.ConvertToProviderExpression.Compile();
        Func<string, string> decrypt = encryptedConverter.ConvertFromProviderExpression.Compile();

        ValueConverter<AuthMode, string> authModeConverter = new(
            mode => encrypt(SerializeAuthMode(mode)),
            encrypted => DeserializeAuthMode(decrypt(encrypted)));

        builder.Property(a => a.AuthMode)
            .HasConversion(authModeConverter)
            .HasColumnType("TEXT")
            .HasColumnName("auth_mode");

        ValueConverter<CredentialValidity, string> validityConverter = new(
            validity => SerializeValidity(validity),
            json => DeserializeValidity(json));

        builder.Property(a => a.Validity)
            .HasConversion(validityConverter)
            .HasColumnType("TEXT")
            .HasColumnName("validity");

        builder.Property(a => a.OAuthAccountEmail)
            .HasMaxLength(ClaudeAccount.MaxOAuthAccountEmailLength)
            .HasColumnName("oauth_account_email");

        builder.Property(a => a.OAuthAccountOrgName)
            .HasMaxLength(ClaudeAccount.MaxOAuthAccountOrgNameLength)
            .HasColumnName("oauth_account_org_name");

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at");
    }

    private static JsonSerializerOptions BuildAuthModeSerializerOptions()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new AuthModeJsonConverter());
        return options;
    }

    private static JsonSerializerOptions BuildValiditySerializerOptions()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new CredentialValidityJsonConverter());
        return options;
    }

    private static string SerializeAuthMode(AuthMode mode)
        => JsonSerializer.Serialize(mode, AuthModeSerializerOptions);

    private static AuthMode DeserializeAuthMode(string json)
        => JsonSerializer.Deserialize<AuthMode>(json, AuthModeSerializerOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize AuthMode from JSON: {json}");

    private static string SerializeValidity(CredentialValidity validity)
        => JsonSerializer.Serialize(validity, ValiditySerializerOptions);

    private static CredentialValidity DeserializeValidity(string json)
        => JsonSerializer.Deserialize<CredentialValidity>(json, ValiditySerializerOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize CredentialValidity from JSON: {json}");
}
