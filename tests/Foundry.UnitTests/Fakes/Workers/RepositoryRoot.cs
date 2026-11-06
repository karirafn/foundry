namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>Locates the repository checkout the tests run from, by walking up to <c>Foundry.slnx</c>.</summary>
internal static class RepositoryRoot
{
    public static string Path { get; } = Find();

    /// <summary>The worker image build context (<c>workers/</c>), where the role catalog lives.</summary>
    public static string WorkersDirectory => System.IO.Path.Combine(Path, "workers");

    private static string Find()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "Foundry.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"No Foundry.slnx found above {AppContext.BaseDirectory}.");
    }
}
