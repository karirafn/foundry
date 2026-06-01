using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features;

public sealed record ReviewFeedback(IReadOnlyList<ReviewComment> Comments);
