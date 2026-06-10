namespace Foundry.Modules.Settings.Infrastructure;

public interface IFileSystem
{
    bool FileExists(string path);
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);
}
