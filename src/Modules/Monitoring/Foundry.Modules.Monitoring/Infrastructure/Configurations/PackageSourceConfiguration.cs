using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

internal sealed class PackageSourceConfiguration : IEntityTypeConfiguration<PackageSource>
{
    private const int UrlMaxLength = 2000;

    public void Configure(EntityTypeBuilder<PackageSource> builder)
    {
        builder.ToTable("package_sources");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasConversion(new StronglyTypedIdValueConverter<PackageSourceId>())
            .HasColumnName("id");

        builder.Property(s => s.ServiceIndexUrl)
            .HasConversion(url => url.AbsoluteUri, value => new Uri(value, UriKind.Absolute))
            .HasMaxLength(UrlMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("url");

        builder.HasIndex(s => s.ServiceIndexUrl)
            .IsUnique()
            .HasDatabaseName("ix_package_sources_url");

        builder.HasMany(s => s.Probes)
            .WithOne()
            .HasForeignKey(p => p.PackageSourceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Probes)
            .HasField("_probes")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

    }
}

internal sealed class PackageSourceProbeConfiguration : IEntityTypeConfiguration<PackageSourceProbe>
{
    private const int CredentialKeyMaxLength = 100;
    private const int OutcomeMaxLength = 20;
    private const int ReasonMaxLength = 500;

    public void Configure(EntityTypeBuilder<PackageSourceProbe> builder)
    {
        builder.ToTable("package_source_probes");

        builder.HasKey(p => new { p.PackageSourceId, p.CredentialKey });

        builder.Property(p => p.PackageSourceId)
            .HasConversion(new StronglyTypedIdValueConverter<PackageSourceId>())
            .HasColumnName("package_source_id");

        builder.Property(p => p.CredentialKey)
            .HasMaxLength(CredentialKeyMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("credential_key");

        builder.Property(p => p.TokenVersion)
            .HasColumnName("token_version");

        builder.Property(p => p.CheckedAt)
            .HasColumnName("checked_at");

        builder.Property<string>("_outcome")
            .HasMaxLength(OutcomeMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("outcome");

        builder.Property<int?>("_httpStatus")
            .HasColumnName("http_status");

        builder.Property<string?>("_reason")
            .HasMaxLength(ReasonMaxLength)
            .HasColumnName("reason");

        builder.Ignore(p => p.Verdict);
    }
}
