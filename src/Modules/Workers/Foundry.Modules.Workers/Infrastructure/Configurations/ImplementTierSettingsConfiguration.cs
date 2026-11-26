using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Workers.Infrastructure.Configurations;

public sealed class ImplementTierSettingsConfiguration : IEntityTypeConfiguration<ImplementTierSettings>
{
    internal const string RoutineToken = "routine";
    internal const string DemandingToken = "demanding";

    private const int DemandMaxLength = 20;
    private const int EffortMaxLength = 20;

    public void Configure(EntityTypeBuilder<ImplementTierSettings> builder)
    {
        builder.ToTable("implement_tier_settings");

        builder.HasKey(s => s.Demand);

        builder.Property(s => s.Demand)
            .HasConversion(new ValueConverter<ImplementDemand, string>(
                demand => demand == ImplementDemand.Routine ? RoutineToken : DemandingToken,
                token => token == RoutineToken ? ImplementDemand.Routine : ImplementDemand.Demanding))
            .HasMaxLength(DemandMaxLength)
            .IsUnicode(false)
            .HasColumnName("demand");

        builder.Property(s => s.Model)
            .HasMaxLength(WorkerRoleSettings.MaxModelLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("model");

        builder.Property(s => s.Effort)
            .HasMaxLength(EffortMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("effort");
    }
}
