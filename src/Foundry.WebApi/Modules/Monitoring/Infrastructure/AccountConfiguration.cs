using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.WebApi.Modules.Monitoring.Infrastructure;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    private const int NameMaxLength = 200;
    private const int SecretKeyNameMaxLength = 200;
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

        builder.Property(a => a.SecretKeyName)
            .HasMaxLength(SecretKeyNameMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("secret_key_name");

        builder.Property(a => a.BaseUrl)
            .HasConversion(
                url => url.ToString(),
                value => new Uri(value))
            .HasMaxLength(BaseUrlMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("base_url");

        builder.HasDiscriminator<string>("type")
            .HasValue<GitHubAccount>("github")
            .IsComplete(true);

        builder.Property<string>("type")
            .HasMaxLength(DiscriminatorMaxLength)
            .HasColumnName("type");
    }
}
