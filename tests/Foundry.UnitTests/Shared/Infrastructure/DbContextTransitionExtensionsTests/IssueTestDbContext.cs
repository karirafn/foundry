using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.UnitTests.Shared.Infrastructure.DbContextTransitionExtensionsTests;

internal sealed class IssueTestDbContext(DbContextOptions<IssueTestDbContext> options) : DbContext(options)
{
    public DbSet<Issue> Issues => Set<Issue>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Issue>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id)
                .HasConversion(new StronglyTypedIdValueConverter<IssueId>());
            builder.Property(x => x.MonitoredRepositoryId)
                .HasConversion(new StronglyTypedIdValueConverter<MonitoredRepositoryId>());
            builder.OwnsOne(x => x.Author, owned =>
            {
                owned.Property(a => a.Value).HasColumnName("Author");
            });
            builder.OwnsOne(x => x.Url, owned =>
            {
                owned.Property(u => u.Value).HasColumnName("Url");
            });
            builder.Property(x => x.Labels)
                .HasConversion(
                    new ValueConverter<IReadOnlyList<string>, string>(
                        v => string.Join(',', v),
                        v => v.Length == 0
                            ? Array.Empty<string>()
                            : v.Split(',', StringSplitOptions.RemoveEmptyEntries)));

            builder.Property(x => x.IssueKind)
                .HasConversion(
                    new ValueConverter<IssueKind, string>(
                        kind => kind.Value,
                        value => IssueKind.FromLabel(value)));

            builder.HasDiscriminator<string>("State")
                .HasValue<DetectedIssue>("detected")
                .HasValue<QueuedIssue>("queued");
        });
    }
}
