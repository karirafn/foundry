using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Workers.Infrastructure.Configurations;

public sealed class DispatchReservationConfiguration : IEntityTypeConfiguration<DispatchReservation>
{
    public void Configure(EntityTypeBuilder<DispatchReservation> builder)
    {
        builder.ToTable("dispatch_reservations");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasConversion(new StronglyTypedIdValueConverter<WorkerRunId>())
            .HasColumnName("id");

        builder.Property(r => r.ReservedAt)
            .HasColumnName("reserved_at");
    }
}
