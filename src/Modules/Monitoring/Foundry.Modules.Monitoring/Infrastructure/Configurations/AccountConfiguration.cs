using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

internal sealed class AccountConfiguration(
    IDataProtectionProvider dataProtectionProvider,
    ILogger<EncryptedStringConverter>? encryptedStringConverterLogger = null)
    : IEntityTypeConfiguration<Account>
{
    private const int NameMaxLength = 200;
    private const int TokenMaxLength = 2000;
    private const int BaseUrlMaxLength = 2000;
    private const int DiscriminatorMaxLength = 20;

    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasConversion(new StronglyTypedIdValueConverter<AccountId>())
            .HasColumnName("id");

        builder.Property(a => a.Name)
            .HasMaxLength(NameMaxLength)
            .IsUnicode(true)
            .IsRequired()
            .HasColumnName("name");

        EncryptedStringConverter encryptedConverter = new(dataProtectionProvider, encryptedStringConverterLogger);

        builder.Property(a => a.Token)
            .HasConversion(encryptedConverter)
            .HasMaxLength(TokenMaxLength)
            .IsUnicode(false)
            .HasColumnName("token");

        builder.Property(a => a.BaseUrl)
            .HasConversion(
                url => url.Value.ToString(),
                value => BaseUrl.FromPersistedString(value))
            .HasMaxLength(BaseUrlMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("base_url");

        builder.HasDiscriminator<string>("type")
            .HasValue<GitHubAccount>(AccountDiscriminators.GitHub)
            .HasValue<GitLabAccount>(AccountDiscriminators.GitLab)
            .IsComplete(true);

        builder.Property<string>("type")
            .HasMaxLength(DiscriminatorMaxLength)
            .HasColumnName("type");
    }
}
