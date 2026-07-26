using Microsoft.Extensions.Options;

namespace Foundry.Shared.Infrastructure.Outbox;

internal sealed class OutboxOptionsValidator : IValidateOptions<OutboxOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxOptions options)
    {
        List<string> failures = [];

        if (options.BatchSize <= 0)
        {
            failures.Add("Outbox:BatchSize must be greater than zero.");
        }

        if (options.MaxAttempts <= 0)
        {
            failures.Add("Outbox:MaxAttempts must be greater than zero.");
        }

        if (options.TickInterval <= TimeSpan.Zero)
        {
            failures.Add("Outbox:TickInterval must be greater than zero.");
        }

        if (options.RetentionWindow <= TimeSpan.Zero)
        {
            failures.Add("Outbox:RetentionWindow must be greater than zero.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
