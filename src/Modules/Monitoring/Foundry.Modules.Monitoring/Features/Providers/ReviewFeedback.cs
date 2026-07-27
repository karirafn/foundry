using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features.Providers;

internal sealed record ReviewFeedback(IReadOnlyList<ReviewComment> Comments);
