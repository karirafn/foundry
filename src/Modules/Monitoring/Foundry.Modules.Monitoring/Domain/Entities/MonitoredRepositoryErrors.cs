using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Entities;

internal static class MonitoredRepositoryErrors
{
    internal const string InvalidMaxConcurrentWorkersCode = "Repository.InvalidMaxConcurrentWorkers";
    internal const string PollIntervalNotPositiveCode = "Repository.PollIntervalNotPositive";
    internal const string PollIntervalTooLargeCode = "Repository.PollIntervalTooLarge";

    internal static Error InvalidMaxConcurrentWorkers(int value) =>
        new(
            InvalidMaxConcurrentWorkersCode,
            $"Max concurrent workers must be between {MonitoredRepository.MinMaxConcurrentWorkers} and {MonitoredRepository.MaxMaxConcurrentWorkers}, but was {value}.");

    internal static Error PollIntervalNotPositive() =>
        new(PollIntervalNotPositiveCode, "Poll interval must be a positive number of seconds.");

    internal static Error PollIntervalTooLarge(int maxSeconds) =>
        new(PollIntervalTooLargeCode, $"Poll interval must not exceed {maxSeconds} seconds.");
}
