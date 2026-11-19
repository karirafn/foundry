using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

internal sealed class MonitoredRepositoryConfiguration(IDataProtectionProvider dataProtectionProvider)
    : IEntityTypeConfiguration<MonitoredRepository>
{
    /// <summary>
    /// The named query filter hiding removed repositories (ADR 0117). A lookup that must see a removed repository
    /// bypasses it by this name.
    /// </summary>
    internal const string RemovedRepositoryFilter = nameof(RemovedRepositoryFilter);

    private const int SlugMaxLength = 500;
    private const int HostMaxLength = 253;
    private const int EligibilityStatusMaxLength = 20;
    private const int WebhookStatusMaxLength = 20;

    // A lowercase hex SHA-256 (WebhookSpecStamp).
    private const int WebhookSpecStampLength = 64;
    private const int BlobShaMaxLength = 64;
    private const int ETagMaxLength = 200;

    private static readonly JsonSerializerOptions EligibilitySerializerOptions = new();

    private static RepositoryEligibility DeserializeEligibility(string json)
    {
        return JsonSerializer.Deserialize<RepositoryEligibility>(json, EligibilitySerializerOptions)
            ?? throw new InvalidOperationException("Failed to deserialize eligibility column of MonitoredRepository.");
    }

    private static WriteProbeVerdict DeserializeWriteProbeVerdict(string? json)
    {
        if (json is null)
        {
            return new WriteProbeVerdict.Unknown();
        }

        try
        {
            return JsonSerializer.Deserialize<WriteProbeVerdict>(json, EligibilitySerializerOptions)
                ?? new WriteProbeVerdict.Unknown();
        }
        catch (JsonException)
        {
            return new WriteProbeVerdict.Unknown();
        }
    }

    public void Configure(EntityTypeBuilder<MonitoredRepository> builder)
    {
        builder.ToTable("monitored_repositories");

        builder.HasKey(r => r.Id);

        builder.HasQueryFilter(RemovedRepositoryFilter, r => r.RemovedAt == null);

        builder.Property(r => r.Id)
            .HasConversion(new StronglyTypedIdValueConverter<MonitoredRepositoryId>())
            .HasColumnName("id");

        builder.Property(r => r.Slug)
            .HasConversion(
                slug => slug.ToString(),
                value => ParseSlug(value))
            .HasMaxLength(SlugMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("slug");

        builder.Property(r => r.Host)
            .HasMaxLength(HostMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("host");

        builder.Property(r => r.PollInterval)
            .HasColumnName("poll_interval");

        builder.Property(r => r.IsActive)
            .HasColumnName("is_active");

        builder.Property(r => r.ApplyBranchRules)
            .HasColumnName("apply_branch_rules");

        builder.Property(r => r.RebaseAfterRun)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .HasColumnName("rebase_after_run");

        builder.Property(r => r.MergeWhenChecksPass)
            .HasDefaultValue(false)
            .HasColumnName("merge_when_checks_pass");

        builder.Property(r => r.ReviewPullRequests)
            .HasDefaultValue(true)
            .HasSentinel(true)
            .HasColumnName("review_pull_requests");

        builder.Property(r => r.MaxConcurrentWorkers)
            .HasDefaultValue(MonitoredRepository.DefaultMaxConcurrentWorkers)
            .HasColumnName("max_concurrent_workers");

        builder.Property(r => r.Position)
            .HasDefaultValue(0)
            .HasColumnName("position");

        builder.HasIndex(r => r.Position)
            .IsUnique()
            // A removed repository keeps its last Position but is out of the order the survivors are renumbered in.
            .HasFilter("removed_at IS NULL")
            .HasDatabaseName("ix_monitored_repositories_position");

        builder.Property(r => r.LastPolledAt)
            .HasColumnName("last_polled_at");

        builder.Property(r => r.UntrackSuppressedSince)
            .HasColumnName("untrack_suppressed_since");

        builder.Property(r => r.RemovedAt)
            .HasColumnName("removed_at");

        ValueConverter<RepositoryEligibility?, string?> eligibilityConverter = new(
            eligibility => eligibility == null
                ? null
                : JsonSerializer.Serialize(eligibility, EligibilitySerializerOptions),
            json => json == null
                ? null
                : DeserializeEligibility(json));

        builder.Property(r => r.Eligibility)
            .HasConversion(eligibilityConverter)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("eligibility");

        builder.Property(r => r.EligibilityStatus)
            .HasMaxLength(EligibilityStatusMaxLength)
            .IsUnicode(false)
            .HasDefaultValue("unreachable")
            .HasColumnName("eligibility_status");

        builder.HasIndex(r => r.EligibilityStatus)
            .HasDatabaseName("ix_monitored_repositories_eligibility_status");

        ValueConverter<WriteProbeVerdict, string?> writeProbeVerdictConverter = new(
            verdict => JsonSerializer.Serialize(verdict, EligibilitySerializerOptions),
            json => DeserializeWriteProbeVerdict(json));

        builder.Property(r => r.WriteProbeVerdict)
            .HasConversion(writeProbeVerdictConverter)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .IsRequired(false)
            .HasColumnName("write_probe_verdict");

        builder.HasIndex(r => new { r.Host, r.Slug })
            .IsUnique()
            .HasDatabaseName("ix_monitored_repositories_host_slug");

        builder.Property(r => r.WebhookHookId)
            .HasMaxLength(MonitoredRepository.WebhookHookIdLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("webhook_hook_id");

        builder.HasIndex(r => r.WebhookHookId)
            .IsUnique()
            .HasDatabaseName("ix_monitored_repositories_webhook_hook_id");

        builder.Property(r => r.WebhookSecret)
            .HasConversion(new WebhookSecretConverter(dataProtectionProvider))
            .HasColumnType("TEXT")
            .IsRequired(false)
            .HasColumnName("webhook_secret");

        builder.Property(r => r.WebhookStatus)
            .HasConversion<string>()
            .HasMaxLength(WebhookStatusMaxLength)
            .IsUnicode(false)
            .HasDefaultValue(WebhookStatus.NotRegistered)
            .HasSentinel(WebhookStatus.NotConfigured)
            .HasColumnName("webhook_status");

        builder.Property(r => r.WebhookSpecStamp)
            .HasMaxLength(WebhookSpecStampLength)
            .IsUnicode(false)
            .IsRequired(false)
            .HasColumnName("webhook_spec_stamp");

        builder.Property(r => r.WebhookRegistrationFailedAt)
            .HasColumnName("webhook_registration_failed_at");

        ValueConverter<IReadOnlyList<DeclaredPackageSource>, string> packageSourcesConverter = new(
            sources => SerializePackageSources(sources),
            json => DeserializePackageSources(json));

        ValueComparer<IReadOnlyList<DeclaredPackageSource>> packageSourcesComparer = new(
            (left, right) => left!.SequenceEqual(right!),
            sources => sources.Aggregate(0, (hash, source) => HashCode.Combine(hash, source)),
            sources => sources.ToList());

        builder.Property(r => r.DeclaredPackageSources)
            .HasConversion(packageSourcesConverter, packageSourcesComparer)
            .HasColumnType("TEXT")
            .HasDefaultValue(Array.Empty<DeclaredPackageSource>())
            .IsRequired()
            .HasColumnName("declared_package_sources");

        builder.Property(r => r.RootNuGetConfigText)
            .HasColumnType("TEXT")
            .HasColumnName("root_nuget_config_text");

        builder.Property(r => r.RootNuGetConfigBlobSha)
            .HasMaxLength(BlobShaMaxLength)
            .IsUnicode(false)
            .HasColumnName("root_nuget_config_blob_sha");

        builder.Property(r => r.RootNuGetConfigInvalidReason)
            .HasColumnType("TEXT")
            .HasColumnName("root_nuget_config_invalid_reason");

        builder.Property(r => r.RootListingETag)
            .HasMaxLength(ETagMaxLength)
            .IsUnicode(false)
            .HasColumnName("root_listing_etag");

        builder.Ignore(r => r.RootNuGetConfig);
    }


    private static string SerializePackageSources(IReadOnlyList<DeclaredPackageSource> sources) =>
        JsonSerializer.Serialize(
            sources.Select(source => new PackageSourceRow(source.Key, source.ServiceIndexUrl.AbsoluteUri)).ToList(),
            EligibilitySerializerOptions);

    /// <summary>
    /// Rebuilds each stored source through <see cref="DeclaredPackageSource.Create"/>, so a row that no longer
    /// passes its rules is dropped rather than read back as a source.
    /// </summary>
    private static List<DeclaredPackageSource> DeserializePackageSources(string json)
    {
        List<PackageSourceRow> rows = JsonSerializer.Deserialize<List<PackageSourceRow>>(json, EligibilitySerializerOptions) ?? [];

        return rows
            .Select(row => DeclaredPackageSource.Create(row.Key, row.Url))
            .OfType<Result<DeclaredPackageSource>.Success>()
            .Select(success => success.Value)
            .ToList();
    }

    private sealed record PackageSourceRow(string Key, string Url);

    private static RepositorySlug ParseSlug(string value)
    {
        if (RepositorySlug.Create(value) is Result<RepositorySlug>.Success success)
        {
            return success.Value;
        }

        throw new InvalidOperationException(
            $"Persisted slug '{value}' could not be parsed as a RepositorySlug.");
    }
}
