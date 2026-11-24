/** A step's copy: a node on the rail, or a queue wait the rail folds into the node after it. */
export interface StepCopy {
  readonly label: string;
  readonly kind: 'node' | 'wait';
  /** What the step is doing while it runs, for a row's status line. */
  readonly doing: string;
}

const STEPS: Readonly<Record<string, StepCopy>> = {
  detected: { label: 'Detected', kind: 'node', doing: 'Detected' },
  triage_queued: { label: 'Queued for triage', kind: 'wait', doing: 'Queued for triage' },
  triage: { label: 'Triage', kind: 'node', doing: 'Triaging the issue' },
  queued: { label: 'Queued for implement', kind: 'wait', doing: 'Queued for implement' },
  implement: { label: 'Implement', kind: 'node', doing: 'Implementing' },
  publish: { label: 'Publish', kind: 'node', doing: "Publishing the run's commits" },
  rebase: { label: 'Rebase', kind: 'node', doing: 'Rebasing onto the default branch' },
  checks: { label: 'Checks', kind: 'node', doing: 'Waiting for checks' },
  review_queued: { label: 'Queued for review', kind: 'wait', doing: 'Queued for review' },
  review: { label: 'Review', kind: 'node', doing: 'Reviewing the pull request' },
  your_review: { label: 'Your review', kind: 'node', doing: 'Waiting for your review' },
  merge: { label: 'Merge', kind: 'node', doing: 'Merging the pull request' },
  done: { label: 'Done', kind: 'node', doing: 'Done' },
};

/** `mark_ready` → `Mark ready`. */
export function sentenceCase(token: string): string {
  const words = token.replaceAll('_', ' ').trim();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/**
 * The copy for a step token. A token this dashboard has no copy for is a node labelled from the
 * token, so the step is never dropped.
 */
export function stepCopy(token: string): StepCopy {
  if (Object.hasOwn(STEPS, token)) {
    return STEPS[token];
  }
  const label = sentenceCase(token);
  return { label, kind: 'node', doing: label };
}
