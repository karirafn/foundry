using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// A closed dependency path that starts and ends at the same issue, for example 42 → 15 → 31 → 42 where each issue is
/// blocked by the next.
/// </summary>
public sealed record DependencyCycle
{
    private const string ArrowSeparator = " → ";

    private const int MinimumPathLength = 3;

    private DependencyCycle(IReadOnlyList<int> path) => Path = path;

    public IReadOnlyList<int> Path { get; }

    /// <summary>The issue whose new blocker link closed the cycle.</summary>
    public int ClosingBlocker => Path[1];

    public static Result<DependencyCycle> Create(IReadOnlyList<int> path)
    {
        if (path.Count < MinimumPathLength)
        {
            return Result<DependencyCycle>.Fail(DependencyCycleErrors.PathTooShort);
        }

        return new DependencyCycle(path);
    }

    public string Describe() => string.Join(ArrowSeparator, Path.Select((int number) => $"#{number}"));
}

public static class DependencyCycleErrors
{
    public static readonly Error PathTooShort = new(
        "DependencyCycle.PathTooShort",
        "A dependency cycle needs a closed path of at least three entries.");
}
