namespace Foundry.Shared;

// Marker interfaces for TPH state variant aggregates.
// The non-generic IStateMachine is used as the TransitionAsync constraint — all state machine
// variants satisfy it through base-class inheritance without requiring self-referential type parameters.
// The generic IStateMachine<TSelf> is applied to the abstract base (e.g. Issue : IStateMachine<Issue>)
// to carry type information and document the pattern.
#pragma warning disable CA1040
public interface IStateMachine;

public interface IStateMachine<TSelf> : IStateMachine where TSelf : class;
#pragma warning restore CA1040
