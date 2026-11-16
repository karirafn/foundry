using System.Text.Json.Serialization;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// A Package Source monitored repositories declare, as Settings lists it. The registered credential's token is never
/// part of it.
/// </summary>
public sealed record PackageSourceSummary(
    Guid Id,
    string Url,
    IReadOnlyList<PackageSourceRepository> Repositories,
    PackageSourceStatus Status,
    DateTimeOffset? LastCheckedAt,
    PackageSourceCredentialSummary? Credential);

/// <summary>A monitored repository that declares a Package Source.</summary>
public sealed record PackageSourceRepository(Guid Id, string Slug);

/// <summary>The registered Package Source Credential, without its token.</summary>
public sealed record PackageSourceCredentialSummary(string Username, DateTimeOffset UpdatedAt);

/// <summary>Whether Foundry can read a Package Source, as its last probes found.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ReachableViaCredential), typeDiscriminator: "reachableViaCredential")]
[JsonDerivedType(typeof(ReachableViaAccount), typeDiscriminator: "reachableViaAccount")]
[JsonDerivedType(typeof(ReachableAnonymously), typeDiscriminator: "reachableAnonymously")]
[JsonDerivedType(typeof(Unreachable), typeDiscriminator: "unreachable")]
[JsonDerivedType(typeof(Unchecked), typeDiscriminator: "unchecked")]
public abstract record PackageSourceStatus
{
    private PackageSourceStatus() { }

    /// <summary>Readable with the registered Package Source Credential.</summary>
    public sealed record ReachableViaCredential : PackageSourceStatus;

    /// <summary>Readable with the account credential named, on <see cref="Host"/>.</summary>
    public sealed record ReachableViaAccount(string AccountName, string Host) : PackageSourceStatus;

    /// <summary>Readable without a credential: the source is on another host than its repositories' provider.</summary>
    public sealed record ReachableAnonymously : PackageSourceStatus;

    /// <summary>A probe failed. <see cref="HttpStatus"/> is null for a transport failure.</summary>
    public sealed record Unreachable(int? HttpStatus, string Reason) : PackageSourceStatus;

    /// <summary>Not probed yet with the credential Foundry reads it with.</summary>
    public sealed record Unchecked : PackageSourceStatus;
}
