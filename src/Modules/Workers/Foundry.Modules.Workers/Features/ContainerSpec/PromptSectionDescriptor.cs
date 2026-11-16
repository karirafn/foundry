namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>When <see cref="SystemPromptBuilder"/> includes a section in a worker's system prompt.</summary>
internal enum PromptSectionCondition
{
    /// <summary>Every run.</summary>
    Always,

    /// <summary>The worker image's recorded toolchain has <c>csharp-ls</c>; an image with no recorded toolchain has none.</summary>
    DotnetImage,

    /// <summary>The worker image's recorded toolchain has <c>docker</c>; an image with no recorded toolchain has none.</summary>
    DockerImage,

    /// <summary>
    /// The worker image's recorded toolchain has both <c>chromium</c> and <c>playwright-core</c>; an image with no
    /// recorded toolchain has neither. Restricted runs, which cannot execute code, never get it.
    /// </summary>
    BrowserRenderingImage,

    /// <summary>A fresh run on a new branch.</summary>
    Fresh,

    /// <summary>A run continuing an interrupted one on its existing branch.</summary>
    Continuation,

    /// <summary>A revision run addressing review feedback or failing checks on an existing pull request.</summary>
    Revision,

    /// <summary>A revision run resolving a conflict from Foundry's post-run rebase.</summary>
    RebaseConflict,

    /// <summary>The issue has a clarification log: earlier runs asked a person questions (ADR 0096).</summary>
    Clarifications,
}

/// <summary>A system-prompt section Foundry adds to the worker prompt, as shown on the dashboard's role cards.</summary>
internal sealed record PromptSectionDescriptor(string Key, string Title, PromptSectionCondition Condition, string Content);
