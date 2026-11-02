using System.Text.RegularExpressions;

using Foundry.Modules.Workers.Features.ContainerSpec;

using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers.Features;

internal sealed partial class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    [GeneratedRegex(@"^(Bash|Edit|Read|Write|MultiEdit|Glob|Grep)\(.+\)$")]
    private static partial Regex DenyRuleFormatPattern();

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
        foreach (string rule in settings.CiCdDenyRules)
        {
            if (string.IsNullOrWhiteSpace(rule))
            {
                failures.Add("Workers:Settings:CiCdDenyRules entries must be non-empty.");
            }
            else if (!DenyRuleFormatPattern().IsMatch(rule))
            {
                failures.Add(
                    "Workers:Settings:CiCdDenyRules entries must follow the format Tool(pattern:*) " +
                    "where Tool is one of Bash, Edit, Read, Write, MultiEdit, Glob, Grep.");
            }
        }

        foreach (string rule in settings.AdditionalDenyRules)
        {
            if (string.IsNullOrWhiteSpace(rule))
            {
                failures.Add("Workers:Settings:AdditionalDenyRules entries must be non-empty.");
            }
            else if (!DenyRuleFormatPattern().IsMatch(rule))
            {
                failures.Add(
                    "Workers:Settings:AdditionalDenyRules entries must follow the format Tool(pattern:*) " +
                    "where Tool is one of Bash, Edit, Read, Write, MultiEdit, Glob, Grep.");
            }
        }

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
