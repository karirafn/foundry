/** How a load ended. A failed load has already surfaced its own error; callers only branch on it. */
export type LoadOutcome = 'loaded' | 'failed';

/** `loaded` only when every load loaded. */
export function combineOutcomes(outcomes: readonly LoadOutcome[]): LoadOutcome {
  return outcomes.every((outcome) => outcome === 'loaded') ? 'loaded' : 'failed';
}
