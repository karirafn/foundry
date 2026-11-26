using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Domain.Entities;

/// <summary>
/// The Tier Model for one <see cref="ImplementDemand"/> (ADR 0095): the model and optional effort level implement runs
/// of that demand use. Triage and review keep their single <see cref="WorkerRoleSettings"/> row.
/// </summary>
public sealed class ImplementTierSettings
{
    /// <summary>
    /// The effort levels the managed settings <c>effortLevel</c> key accepts, from the Claude Code model-configuration
    /// documentation (<c>max</c> is documented as not accepted there).
    /// </summary>
    public static readonly IReadOnlyList<string> EffortLevels = ["low", "medium", "high", "xhigh"];

    // Private parameterless constructor for EF Core materialization.
    private ImplementTierSettings()
    {
        Demand = ImplementDemand.Demanding;
        Model = string.Empty;
        Effort = string.Empty;
    }

    private ImplementTierSettings(ImplementDemand demand)
    {
        Demand = demand;
        Model = string.Empty;
        Effort = string.Empty;
    }

    public ImplementDemand Demand { get; private set; }

    /// <summary>
    /// The model the tier's runs use: a Claude Code alias or a model ID. Empty means the CLI's account default.
    /// </summary>
    public string Model { get; private set; }

    /// <summary>One of <see cref="EffortLevels"/>, or empty to leave the model's default effort.</summary>
    public string Effort { get; private set; }

    public static ImplementTierSettings Create(ImplementDemand demand) => new(demand);

    /// <summary>Whether <paramref name="effort"/> is empty or one of <see cref="EffortLevels"/>.</summary>
    public static bool IsValidEffort(string effort) =>
        effort.Length == 0 || EffortLevels.Contains(effort, StringComparer.Ordinal);

    /// <summary>Sets the model. The caller validates it against the aliases and the listed models.</summary>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public bool ChangeModel(string model)
    {
        ArgumentNullException.ThrowIfNull(model);
        string trimmed = model.Trim();

        if (trimmed.Length > WorkerRoleSettings.MaxModelLength)
        {
            throw new ArgumentException(
                $"A model must not exceed {WorkerRoleSettings.MaxModelLength} characters.",
                nameof(model));
        }

        if (string.Equals(trimmed, Model, StringComparison.Ordinal))
        {
            return false;
        }

        Model = trimmed;
        return true;
    }

    /// <summary>Sets the effort level; empty leaves the model's default.</summary>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public bool ChangeEffort(string effort)
    {
        ArgumentNullException.ThrowIfNull(effort);
        string trimmed = effort.Trim();

        if (!IsValidEffort(trimmed))
        {
            throw new ArgumentException(
                $"An effort must be empty or one of: {string.Join(", ", EffortLevels)}.",
                nameof(effort));
        }

        if (string.Equals(trimmed, Effort, StringComparison.Ordinal))
        {
            return false;
        }

        Effort = trimmed;
        return true;
    }
}
