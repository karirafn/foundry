using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

internal sealed class CredentialNamespaceConfiguration : IEntityTypeConfiguration<CredentialNamespace>
{
    private const int NamespaceValueMaxLength = 500;
    private const int HostMaxLength = 253;

    public void Configure(EntityTypeBuilder<CredentialNamespace> builder)
    {
        builder.ToTable("credential_namespaces");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id)
            .HasConversion(new StronglyTypedIdValueConverter<CredentialNamespaceId>())
            .HasColumnName("id");

        builder.Property(n => n.CredentialId)
            .HasConversion(new StronglyTypedIdValueConverter<CredentialId>())
            .HasColumnName("credential_id");

        builder.Property(n => n.Host)
            .HasMaxLength(HostMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("host");

        builder.Property(n => n.Value)
            .HasMaxLength(NamespaceValueMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("value");
    }
}
