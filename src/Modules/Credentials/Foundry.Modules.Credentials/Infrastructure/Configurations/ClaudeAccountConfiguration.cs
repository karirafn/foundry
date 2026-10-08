using System.Text.Json;

using Foundry.Modules.Credentials.Domain.Entities;
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
    ILogger<ApiKeyCredentialConverter>? apiKeyCredentialConverterLogger = null)
    : IEntityTypeConfiguration<ClaudeAccount>
{
    private static readonly JsonSerializerOptions AuthModeSerializerOptions = BuildAuthModeSerializerOptions();
    private static readonly JsonSerializerOptions ValiditySerializerOptions = BuildValiditySerializerOptions();
    private static readonly JsonSerializerOptions SpendStateSerializerOptions = BuildSpendStateSerializerOptions();

    public void Configure(EntityTypeBuilder<ClaudeAccount> builder)
    {
        builder.ToTable("claude_account");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasConversion(new StronglyTypedIdValueConverter<ClaudeAccountId>())
            .HasColumnName("id");

        // auth_mode persists the mode discriminator only (type + OAuth subscription_type) as
        // plaintext JSON — no outer encryption. The API key credential lives in a separate
        // encrypted column so that a corrupt api_key decrypts to Unreadable without preventing
        // the row from loading.
        ValueConverter<AuthMode, string> authModeConverter = new(
            mode => SerializeAuthMode(mode),
            json => DeserializeAuthMode(json));

        // _authModeRecord is the persistence-only backing field; AuthMode assembles the full
        // value at read time from _authModeRecord (mode type) + _apiKeyCredential (key).
        builder.Property(a => a.AuthMode)
            .HasField("_authModeRecord")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(authModeConverter)
            .HasColumnType("TEXT")
            .HasColumnName("auth_mode");

        ApiKeyCredentialConverter apiKeyConverter = new(dataProtectionProvider, apiKeyCredentialConverterLogger);

        // _apiKeyCredential: Present → encrypted TEXT; NotConfigured → NULL; Unreadable is
        // a read-only outcome of a failed decrypt and is never written back.
        // Mapped as a field-only property (no CLR property) so EF reads/writes the backing
        // field directly — PropertyAccessMode.Field is required.
        builder.Property<ApiKeyCredential?>("_apiKeyCredential")
            .HasField("_apiKeyCredential")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(apiKeyConverter)
            .HasColumnType("TEXT")
            .IsRequired(false)
            .HasColumnName("api_key");

        ValueConverter<CredentialValidity, string> validityConverter = new(
            validity => SerializeValidity(validity),
            json => DeserializeValidity(json));

        builder.Property(a => a.Validity)
            .HasConversion(validityConverter)
            .HasColumnType("TEXT")
            .HasColumnName("validity");

        ValueConverter<SpendState, string> spendStateConverter = new(
            spendState => SerializeSpendState(spendState),
            json => DeserializeSpendState(json));

        builder.Property(a => a.SpendState)
            .HasConversion(spendStateConverter)
            .HasColumnType("TEXT")
            .HasColumnName("spend_state");

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

    private static JsonSerializerOptions BuildSpendStateSerializerOptions()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new SpendStateJsonConverter());
        return options;
    }

    private static string SerializeAuthMode(AuthMode mode)
        => JsonSerializer.Serialize(mode, AuthModeSerializerOptions);

    private static AuthMode DeserializeAuthMode(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AuthMode>(json, AuthModeSerializerOptions)
                ?? new AuthMode.ApiKey(new ApiKeyCredential.NotConfigured());
        }
        catch (JsonException)
        {
            // Legacy rows may contain encrypted ciphertext in auth_mode rather than plaintext
            // JSON (the old single-column scheme encrypted the full mode blob). Return
            // NotConfigured so the account still loads — the user will need to re-enter their
            // API key, but they are not locked out of the repair UI.
            return new AuthMode.ApiKey(new ApiKeyCredential.NotConfigured());
        }
    }

    private static string SerializeValidity(CredentialValidity validity)
        => JsonSerializer.Serialize(validity, ValiditySerializerOptions);

    private static CredentialValidity DeserializeValidity(string json)
        => JsonSerializer.Deserialize<CredentialValidity>(json, ValiditySerializerOptions)
            ?? throw new InvalidOperationException("Failed to deserialize CredentialValidity from the stored value.");

    private static string SerializeSpendState(SpendState spendState)
        => JsonSerializer.Serialize(spendState, SpendStateSerializerOptions);

    private static SpendState DeserializeSpendState(string json)
        => JsonSerializer.Deserialize<SpendState>(json, SpendStateSerializerOptions)
            ?? throw new InvalidOperationException("Failed to deserialize SpendState from the stored value.");
}
