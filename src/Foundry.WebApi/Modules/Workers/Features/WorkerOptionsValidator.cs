using Microsoft.Extensions.Options;

namespace Foundry.WebApi.Modules.Workers.Features;

internal sealed class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return ValidateOptionsResult.Fail("Workers:ApiKey must be non-empty. Set the Anthropic API key before starting.");
        }

        return ValidateOptionsResult.Success;
    }
}
