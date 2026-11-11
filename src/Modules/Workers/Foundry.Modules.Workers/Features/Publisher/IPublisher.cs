namespace Foundry.Modules.Workers.Features.Publisher;

/// <summary>
/// The Publisher (ADR 0100): a short-lived, model-free container that holds the provider token for one worker run, so
/// no worker container spec carries a provider credential.
/// </summary>
internal interface IPublisher
{
    /// <summary>
    /// Prepare mode: creates the run's Hand-off volume and writes the run's base bundle to it, ready for the worker
    /// to clone. Never throws for a Docker or provider failure; it returns <see cref="PrepareOutcome.Failed"/>.
    /// </summary>
    Task<PrepareOutcome> PrepareAsync(PrepareRequest request, CancellationToken cancellationToken);
}
