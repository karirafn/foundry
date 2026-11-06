using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Domain.Entities;

/// <summary>
/// The operator settings of one <see cref="WorkerRole"/>, keyed by role name (ADR 0095). The model is the only
/// per-role setting: everything else a role runs with ships in the worker image.
/// </summary>
public sealed class WorkerRoleSettings
{
    /// <summary>The longest model value stored; model IDs and aliases are far shorter.</summary>
    public const int MaxModelLength = 200;

    // Private parameterless constructor for EF Core materialization.
    private WorkerRoleSettings()
    {
        Role = WorkerRole.Implement;
        Model = string.Empty;
    }

    private WorkerRoleSettings(WorkerRole role, string model)
    {
        Role = role;
        Model = model;
    }

    public WorkerRole Role { get; private set; }

    /// <summary>
    /// The model the role's runs use: a Claude Code alias or a model ID. Empty means the CLI's account default.
    /// </summary>
    public string Model { get; private set; }

    public static WorkerRoleSettings Create(WorkerRole role) => new(role, string.Empty);

    /// <summary>Sets the model. The caller validates it against the aliases and the listed models.</summary>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public bool ChangeModel(string model)
    {
        ArgumentNullException.ThrowIfNull(model);
        string trimmed = model.Trim();

        if (trimmed.Length > MaxModelLength)
        {
            throw new ArgumentException($"A model must not exceed {MaxModelLength} characters.", nameof(model));
        }

        if (string.Equals(trimmed, Model, StringComparison.Ordinal))
        {
            return false;
        }

        Model = trimmed;
        return true;
    }
}
