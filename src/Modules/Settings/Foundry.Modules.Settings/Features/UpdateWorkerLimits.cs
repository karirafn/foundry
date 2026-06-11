using Foundry.Modules.Settings.Domain;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Features;

internal static class UpdateWorkerLimits
{
    private const int MinMaxConcurrent = 1;
    private const int MaxMaxConcurrent = 20;
    private const int MinTimeoutMinutes = 1;
    private const int MaxTimeoutMinutes = 1440;

    internal sealed record Command(int MaxConcurrent, int TimeoutMinutes);

    internal sealed class Validator : ICommandValidator<Command>
    {
        public Result Validate(Command command)
        {
            if (command.MaxConcurrent < MinMaxConcurrent || command.MaxConcurrent > MaxMaxConcurrent)
            {
                return SettingsErrors.InvalidMaxConcurrent(command.MaxConcurrent);
            }

            if (command.TimeoutMinutes < MinTimeoutMinutes || command.TimeoutMinutes > MaxTimeoutMinutes)
            {
                return SettingsErrors.InvalidTimeout(command.TimeoutMinutes);
            }

            return Result.Ok();
        }
    }
}
