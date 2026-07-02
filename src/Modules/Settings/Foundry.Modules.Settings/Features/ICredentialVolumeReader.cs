namespace Foundry.Modules.Settings.Features;

/// <summary>
/// Reads the OAuth credential file from the managed Docker volume to determine
/// honest credential status from local data only.
/// </summary>
internal interface ICredentialVolumeReader
{
    Task<CredentialVolumeStatus> ReadStatusAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Describes the credential status derived exclusively from local volume data.
/// Present indicates the credential file was found. ExpiresAt and SubscriptionType
/// are null when the file is absent or cannot be parsed.
/// </summary>
internal sealed record CredentialVolumeStatus(
    bool Present,
    DateTimeOffset? ExpiresAt,
    string? SubscriptionType);
