using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>What a review run's prompt shows beyond the issue: the facts code computed, and the pull request's description.</summary>
internal sealed record ReviewPromptInput(SuppliedReviewFacts Facts, LivePullRequestBody Description);
