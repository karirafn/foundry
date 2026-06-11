using System.Runtime.InteropServices;
using System.Text.Json;

using Foundry.Modules.Settings.Domain;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Infrastructure;

internal sealed class FileSystemOAuthCredentialScanner : IOAuthCredentialScanner
{
    private const string CredentialsFileName = ".credentials.json";
    private const string ClaudeFolderName = ".claude";
    private const string ClaudeWindowsFolderName = "claude";

    private readonly IFileSystem _fileSystem;
    private readonly IReadOnlyList<string> _searchPaths;

    public FileSystemOAuthCredentialScanner(IFileSystem fileSystem)
        : this(fileSystem, BuildDefaultSearchPaths())
    {
    }

    internal FileSystemOAuthCredentialScanner(IFileSystem fileSystem, IReadOnlyList<string> searchPaths)
    {
        _fileSystem = fileSystem;
        _searchPaths = searchPaths;
    }

    public async Task<Result<OAuthCredentials>> ScanAsync(CancellationToken cancellationToken)
    {
        foreach (string path in _searchPaths)
        {
            if (!_fileSystem.FileExists(path))
            {
                continue;
            }

            Result<OAuthCredentials> parseResult = await TryParseCredentialsAsync(path, cancellationToken);
            if (parseResult.IsSuccess)
            {
                return parseResult;
            }
        }

        return Result<OAuthCredentials>.Fail(SettingsErrors.OAuthCredentialsNotFound);
    }

    private static List<string> BuildDefaultSearchPaths()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        List<string> paths = [Path.Combine(userProfile, ClaudeFolderName, CredentialsFileName)];

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            paths.Add(Path.Combine(appData, ClaudeWindowsFolderName, CredentialsFileName));
        }

        return paths;
    }

    private async Task<Result<OAuthCredentials>> TryParseCredentialsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            string json = await _fileSystem.ReadAllTextAsync(path, cancellationToken);
            return ParseCredentials(json);
        }
        catch (IOException)
        {
            return Result<OAuthCredentials>.Fail(SettingsErrors.OAuthCredentialsNotFound);
        }
        catch (JsonException)
        {
            return Result<OAuthCredentials>.Fail(SettingsErrors.OAuthCredentialsNotFound);
        }
    }

    private static Result<OAuthCredentials> ParseCredentials(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("claudeAiOauth", out JsonElement oauthElement))
        {
            return Result<OAuthCredentials>.Fail(SettingsErrors.OAuthCredentialsNotFound);
        }

        if (!TryGetString(oauthElement, "accessToken", out string? accessToken) ||
            !TryGetString(oauthElement, "refreshToken", out string? refreshToken) ||
            !TryGetString(oauthElement, "subscriptionType", out string? subscriptionType) ||
            !TryGetDateTimeOffset(oauthElement, "expiresAt", out DateTimeOffset expiresAt))
        {
            return Result<OAuthCredentials>.Fail(SettingsErrors.OAuthCredentialsNotFound);
        }

        return new OAuthCredentials(accessToken, refreshToken, expiresAt, subscriptionType);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out JsonElement prop) &&
            prop.GetString() is string str &&
            str.Length > 0)
        {
            value = str;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetDateTimeOffset(JsonElement element, string propertyName, out DateTimeOffset value)
    {
        if (element.TryGetProperty(propertyName, out JsonElement prop))
        {
            if (prop.ValueKind == JsonValueKind.String && prop.TryGetDateTimeOffset(out DateTimeOffset dto))
            {
                value = dto;
                return true;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out long epochMs))
            {
                value = DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
                return true;
            }
        }

        value = default;
        return false;
    }
}
