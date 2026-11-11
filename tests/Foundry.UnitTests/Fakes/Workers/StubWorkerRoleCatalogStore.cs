using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Roles;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// An <see cref="IWorkerRoleCatalogStore"/> preloaded with an implement-role catalog, so dispatch tests can supply
/// role prompts without an image build.
/// </summary>
internal sealed class StubWorkerRoleCatalogStore(WorkerRoleCatalog? current) : IWorkerRoleCatalogStore
{
    internal const string DefaultSystemPrompt = "You are implementing issue #{issueNumber}.\n\n{issueContent}";
    internal const string DefaultTaskPrompt = "Implement issue #{issueNumber}.";
    internal const string DefaultPrinciples = "Engineering principles.";
    internal const string DefaultImageTag = "stub-image:v1";

    public WorkerRoleCatalog? Current { get; private set; } = current;

    /// <summary>A store holding an implement-role catalog with the given prompts and image.</summary>
    public static StubWorkerRoleCatalogStore With(
        string systemPrompt = DefaultSystemPrompt,
        string taskPrompt = DefaultTaskPrompt,
        string imageTag = DefaultImageTag,
        string? imageId = null,
        string principles = DefaultPrinciples,
        WorkerCapability capability = WorkerCapability.Full) =>
        new(Catalog(systemPrompt, taskPrompt, imageTag, imageId, principles, capability));

    /// <summary>A store whose catalog has not been loaded, as before the first image build.</summary>
    public static StubWorkerRoleCatalogStore Empty() => new(null);

    public static WorkerRoleCatalog Catalog(
        string systemPrompt = DefaultSystemPrompt,
        string taskPrompt = DefaultTaskPrompt,
        string imageTag = DefaultImageTag,
        string? imageId = null,
        string principles = DefaultPrinciples,
        WorkerCapability capability = WorkerCapability.Full)
    {
        RolePlugin implementPlugin = new(WorkerRole.Implement.Name, "Implements an issue.", [], []);
        RolePlugin sharedPlugin = new(WorkerRole.SharedFolder, "Shared.", [], []);

        WorkerRoleCatalogContent content = new(
            [new WorkerRoleDefinition(WorkerRole.Implement, capability, implementPlugin, systemPrompt, taskPrompt, VerdictSchema: null)],
            sharedPlugin,
            principles);

        return new WorkerRoleCatalog(
            content,
            new WorkerImageProvenance(imageTag, imageId, DateTimeOffset.UnixEpoch, Commit: "abc1234", Verified: true));
    }

    public void Publish(WorkerRoleCatalog catalog) => Current = catalog;
}
