using Microsoft.AspNetCore.DataProtection;

namespace Foundry.Testing;

public sealed class TemporaryKeyRing : IDisposable
{
    public TemporaryKeyRing()
    {
        Directory = System.IO.Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"foundry-dp-keys-{Guid.NewGuid():N}"));
    }

    public DirectoryInfo Directory { get; }

    public IDataProtectionProvider ProviderFor(string applicationName)
    {
        return DataProtectionProvider.Create(Directory, builder => builder.SetApplicationName(applicationName));
    }

    public void Dispose()
    {
        Directory.Delete(recursive: true);
    }
}
