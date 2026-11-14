using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

internal sealed class CredentialConfiguration(
    IDataProtectionProvider dataProtectionProvider)
    : IEntityTypeConfiguration<Credential>
{
    private const int TokenMaxLength = 2000;
    private const int BaseUrlMaxLength = 2000;
    private const int DiscriminatorMaxLength = 20;
    private const int HostMaxLength = 253;

    public void Configure(EntityTypeBuilder<Credential> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasConversion(new StronglyTypedIdValueConverter<CredentialId>())
            .HasColumnName("id");

        builder.Property(a => a.Name)
            .HasMaxLength(AccountsDatabaseHelpers.AccountNameMaxLength)
            .IsUnicode(true)
            .IsRequired()
            .HasColumnName("name");

        ProviderTokenConverter providerTokenConverter = new(dataProtectionProvider);

        builder.Property(a => a.Token)
            .HasConversion(providerTokenConverter)
            .HasMaxLength(TokenMaxLength)
            .IsUnicode(false)
            .HasColumnName("token");

        // The only concurrency token in the model: a token verdict loaded before a rotation must not
        // be saved after it, so a save whose loaded version no longer matches is rejected.
        builder.Property(a => a.TokenVersion)
            .IsConcurrencyToken()
            .HasDefaultValue(0)
            .HasColumnName("token_version");

        // Both null means the token is Valid (TokenValidity is derived from these two columns).
        builder.Property<DateTimeOffset?>("_tokenInvalidSince")
            .HasColumnName("token_invalid_since");

        builder.Property<DateTimeOffset?>("_tokenCheckedAt")
            .HasColumnName("token_checked_at");

        builder.Ignore(a => a.TokenValidity);

        // Same encryption mechanism as the account token; the column is nullable because the
        // branch-rules token is optional.
        builder.Property(a => a.BranchRulesToken)
            .HasConversion(providerTokenConverter)
            .HasMaxLength(TokenMaxLength)
            .IsUnicode(false)
            .HasColumnName("branch_rules_token");

        builder.Property(a => a.BaseUrl)
            .HasConversion(
                url => url.Value.ToString(),
                value => BaseUrl.FromPersistedString(value))
            .HasMaxLength(BaseUrlMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("base_url");

        builder.Property(a => a.Host)
            .HasMaxLength(HostMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("host");

        builder.HasDiscriminator<string>("type")
            .HasValue<GitHubCredential>(CredentialDiscriminators.GitHub)
            .HasValue<GitLabCredential>(CredentialDiscriminators.GitLab)
            .IsComplete(true);

        builder.Property<string>("type")
            .HasMaxLength(DiscriminatorMaxLength)
            .HasColumnName("type");

        builder.HasMany(a => a.Namespaces)
            .WithOne()
            .HasForeignKey(n => n.CredentialId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
