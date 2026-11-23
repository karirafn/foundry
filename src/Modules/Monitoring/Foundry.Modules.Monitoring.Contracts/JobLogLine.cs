namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// One line of a CI job log. <see cref="Number"/> counts lines from 1, and <see cref="Offset"/> is the byte offset in
/// the whole log where the line starts, so a reader can tell which lines a byte range of the log already holds.
/// </summary>
public sealed record JobLogLine(long Number, long Offset, string Text);
