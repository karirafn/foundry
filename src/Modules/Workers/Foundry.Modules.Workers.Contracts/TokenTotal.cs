namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// A token metric summed across runs, with explicit counts of the runs whose figure was known
/// and unknown. <see cref="Total"/> is the sum of the known runs only, so it is exact when
/// <see cref="UnknownRunCount"/> is zero and a lower bound otherwise.
/// </summary>
/// <remarks>
/// When <see cref="KnownRunCount"/> is zero and <see cref="UnknownRunCount"/> is positive, no
/// figure is known at all: <see cref="Total"/> is zero but must be presented as unknown, never as
/// zero. When both counts are zero no run contributed and <see cref="Total"/> is a true zero.
/// </remarks>
/// <param name="Total">Sum of the known runs' figures.</param>
/// <param name="KnownRunCount">Number of contributing runs whose figure is known.</param>
/// <param name="UnknownRunCount">Number of contributing runs whose figure is unknown.</param>
public sealed record TokenTotal(long Total, int KnownRunCount, int UnknownRunCount)
{
    /// <summary>No contributing runs.</summary>
    public static TokenTotal None { get; } = new(0L, 0, 0);
}
