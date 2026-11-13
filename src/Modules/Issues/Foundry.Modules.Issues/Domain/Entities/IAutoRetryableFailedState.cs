using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities;

/// <summary>
/// A failed issue state the transient retry service retries automatically when its
/// <see cref="FailureCategory"/> carries an <see cref="AutomaticRetrySchedule"/>.
/// </summary>
public interface IAutoRetryableFailedState
{
    FailureCategory FailureCategory { get; }

    DateTimeOffset FailedAt { get; }
}
