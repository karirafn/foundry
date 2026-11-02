using Foundry.Modules.Workers.Features.ContainerSpec;

using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers.Features;

internal sealed class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerOptions options)
    {
        List<string> failures = [];

        ValidateSettings(options.Settings, failures);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateSettings(WorkerSettingsOptions settings, List<string> failures)
    {
        foreach (KeyValuePair<string, List<HookGroup>> hookEvent in settings.Hooks)
        {
            foreach (HookGroup group in hookEvent.Value)
            {
                foreach (HookEntry entry in group.Hooks)
                {
                    if (entry.Type != "command")
                    {
                        failures.Add(
                            $"Workers:Settings:Hooks hook type '{entry.Type}' is not supported. Only \"command\" is supported.");
                    }

                    if (string.IsNullOrWhiteSpace(entry.Command))
                    {
                        failures.Add("Workers:Settings:Hooks hook Command must be non-empty.");
                    }
                }
            }
        }
    }
}
