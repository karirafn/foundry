using Foundry.Modules.Workers.Contracts;

namespace Foundry.Testing;

public static class TriageDecisionBuilder
{
    public static TriageDecision Build(
        string question = "Which retry cap?",
        string decision = "Three attempts.",
        TriageDecisionGround ground = TriageDecisionGround.SettledBusiness,
        IReadOnlyList<string>? evidence = null) =>
        TriageDecision.Create(question, decision, ground, evidence ?? ["src/Retry.cs:8 caps at three"]).ValueOrThrow();
}
