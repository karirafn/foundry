using System.Text.Json;

using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Workers.Infrastructure.Configurations;

public sealed class DispatchPauseConfiguration : IEntityTypeConfiguration<DispatchPause>
{
    public void Configure(EntityTypeBuilder<DispatchPause> builder)
    {
        builder.ToTable("dispatch_pause");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasConversion(new StronglyTypedIdValueConverter<DispatchPauseId>())
            .HasColumnName("id");

        builder.Property(p => p.UsageLimitResetsAt)
            .HasColumnName("usage_limit_resets_at");

        builder.Property(p => p.IsDispatchPaused)
            .HasColumnName("is_dispatch_paused");

        ValueConverter<IReadOnlyList<HostPause>, string> hostPausesConverter = new(
            pauses => SerializeHostPauses(pauses),
            json => DeserializeHostPauses(json));

        ValueComparer<IReadOnlyList<HostPause>> hostPausesComparer = new(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            pauses => pauses.Aggregate(0, (hash, p) => HashCode.Combine(hash, p.GetHashCode())),
            pauses => pauses.ToList());

        // One JSON TEXT column: a handful of hosts at most, always read and written with the other pause reasons.
        builder.Property(p => p.HostPauses)
            .HasConversion(hostPausesConverter, hostPausesComparer)
            .HasColumnType("TEXT")
            .HasColumnName("host_pauses");

        // EF stores NULL for an absent pause without calling the converter, so the lambdas never see null.
        ValueConverter<PackageProxyPause?, string> packageProxyPauseConverter = new(
            pause => SerializePackageProxyPause(pause!),
            json => DeserializePackageProxyPause(json));

        builder.Property(p => p.PackageProxyPause)
            .HasConversion(packageProxyPauseConverter)
            .HasColumnType("TEXT")
            .HasColumnName("package_proxy_pause");

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at");
    }

    private static string SerializeHostPauses(IReadOnlyList<HostPause> pauses)
        => JsonSerializer.Serialize(pauses.Select(p => new StoredHostPause(p.Host, p.PausedAt, p.ProbeAttempts, p.NextProbeAt)));

    private static List<HostPause> DeserializeHostPauses(string json)
        => (JsonSerializer.Deserialize<List<StoredHostPause>>(json) ?? [])
            .Select(p => HostPause.Restore(p.Host, p.PausedAt, p.ProbeAttempts, p.NextProbeAt))
            .ToList();

    private static string SerializePackageProxyPause(PackageProxyPause pause)
        => JsonSerializer.Serialize(new StoredPackageProxyPause(pause.PausedAt, pause.ProbeAttempts, pause.NextProbeAt));

    private static PackageProxyPause DeserializePackageProxyPause(string json)
    {
        StoredPackageProxyPause stored = JsonSerializer.Deserialize<StoredPackageProxyPause>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize the package-proxy pause from JSON: {json}");

        return PackageProxyPause.Restore(stored.PausedAt, stored.ProbeAttempts, stored.NextProbeAt);
    }

    // The stored shape, kept apart from HostPause so the column format never follows a domain refactor.
    private sealed record StoredHostPause(string Host, DateTimeOffset PausedAt, int ProbeAttempts, DateTimeOffset NextProbeAt);

    // The stored shape, kept apart from PackageProxyPause so the column format never follows a domain refactor.
    private sealed record StoredPackageProxyPause(DateTimeOffset PausedAt, int ProbeAttempts, DateTimeOffset NextProbeAt);
}
