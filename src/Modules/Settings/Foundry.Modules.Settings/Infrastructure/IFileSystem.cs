namespace Foundry.Modules.Settings.Infrastructure;

internal interface IFileSystem
{
    bool FileExists(string path);
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);
}
