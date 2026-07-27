using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features.Providers;

public sealed record ReviewFeedback(IReadOnlyList<ReviewComment> Comments);
