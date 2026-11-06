using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Workers.Infrastructure.Configurations;

public sealed class WorkerRoleSettingsConfiguration : IEntityTypeConfiguration<WorkerRoleSettings>
{
    private const int RoleMaxLength = 50;

    public void Configure(EntityTypeBuilder<WorkerRoleSettings> builder)
    {
        builder.ToTable("worker_role_settings");

        builder.HasKey(s => s.Role);

        builder.Property(s => s.Role)
            .HasConversion(new ValueConverter<WorkerRole, string>(role => role.Name, name => WorkerRole.FromStored(name)))
            .HasMaxLength(RoleMaxLength)
            .IsUnicode(false)
            .HasColumnName("role");

        builder.Property(s => s.Model)
            .HasMaxLength(WorkerRoleSettings.MaxModelLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("model");
    }
}
