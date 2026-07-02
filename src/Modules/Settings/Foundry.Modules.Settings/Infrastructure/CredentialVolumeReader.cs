using System.Text.Json;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Settings.Features;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Settings.Infrastructure;

internal sealed class CredentialVolumeReader(
    IVolumeOperations volumeOperations,
    ILogger<CredentialVolumeReader> logger) : ICredentialVolumeReader
{
    private const string VolumeName = "foundry-claude-credentials";
    private const string CredentialsFileName = ".credentials.json";

    public async Task<CredentialVolumeStatus> ReadStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            VolumeResponse volume = await volumeOperations.InspectAsync(VolumeName, cancellationToken);
            string mountpoint = volume.Mountpoint;

            if (string.IsNullOrEmpty(mountpoint))
            {
                return new CredentialVolumeStatus(Present: false, ExpiresAt: null, SubscriptionType: null);
            }

            string filePath = Path.Combine(mountpoint, CredentialsFileName);

            if (!File.Exists(filePath))
            {
                return new CredentialVolumeStatus(Present: false, ExpiresAt: null, SubscriptionType: null);
            }

            return await ParseCredentialFileAsync(filePath, cancellationToken);
        }
        catch (DockerApiException ex)
        {
            logger.LogDebug(ex, "Volume {VolumeName} not found or Docker unavailable", VolumeName);
            return new CredentialVolumeStatus(Present: false, ExpiresAt: null, SubscriptionType: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to read credential volume {VolumeName}", VolumeName);
            return new CredentialVolumeStatus(Present: false, ExpiresAt: null, SubscriptionType: null);
        }
    }

    private static async Task<CredentialVolumeStatus> ParseCredentialFileAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(filePath, cancellationToken);
            return ParseCredentials(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // File present but unreadable/unparseable — honest: present but no parsed fields.
            return new CredentialVolumeStatus(Present: true, ExpiresAt: null, SubscriptionType: null);
        }
    }

    private static CredentialVolumeStatus ParseCredentials(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("claudeAiOauth", out JsonElement oauthElement))
        {
            return new CredentialVolumeStatus(Present: true, ExpiresAt: null, SubscriptionType: null);
        }

        DateTimeOffset? expiresAt = TryGetDateTimeOffset(oauthElement, "expiresAt");
        string? subscriptionType = TryGetString(oauthElement, "subscriptionType");

        return new CredentialVolumeStatus(Present: true, ExpiresAt: expiresAt, SubscriptionType: subscriptionType);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement prop) &&
            prop.GetString() is string str &&
            str.Length > 0)
        {
            return str;
        }

        return null;
    }

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement prop))
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.String && prop.TryGetDateTimeOffset(out DateTimeOffset dto))
        {
            return dto;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out long epochMs))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
        }

        return null;
    }
}
