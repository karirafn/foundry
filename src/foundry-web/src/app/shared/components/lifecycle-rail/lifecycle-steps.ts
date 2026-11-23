/** The stages a lifecycle rail groups its steps under, in order. */
export type RailStage = 'triage' | 'implement' | 'review' | 'merge';

export const RAIL_STAGE_LABELS: Readonly<Record<RailStage, string>> = {
  triage: 'Triage',
  implement: 'Implement',
  review: 'Review',
  merge: 'Merge',
};

/** A step's copy: a node on the rail, or a queue wait drawn as a dot on the connector before the next node. */
export interface StepCopy {
  readonly label: string;
  readonly kind: 'node' | 'wait';
  readonly stage: RailStage;
  /** What the step is doing while it runs, for a row's status line. */
  readonly doing: string;
}

const STEPS: Readonly<Record<string, StepCopy>> = {
  detected: { label: 'Detected', kind: 'node', stage: 'triage', doing: 'Detected' },
  triage_queued: {
    label: 'Queued for triage',
    kind: 'wait',
    stage: 'triage',
    doing: 'Queued for triage',
  },
  triage: { label: 'Triage', kind: 'node', stage: 'triage', doing: 'Triaging the issue' },
  queued: {
    label: 'Queued for implement',
    kind: 'wait',
    stage: 'implement',
    doing: 'Queued for implement',
  },
  implement: { label: 'Implement', kind: 'node', stage: 'implement', doing: 'Implementing' },
  publish: {
    label: 'Publish',
    kind: 'node',
    stage: 'implement',
    doing: "Publishing the run's commits",
  },
  rebase: {
    label: 'Rebase',
    kind: 'node',
    stage: 'implement',
    doing: 'Rebasing onto the default branch',
  },
  checks: { label: 'Checks', kind: 'node', stage: 'implement', doing: 'Waiting for checks' },
  review_queued: {
    label: 'Queued for review',
    kind: 'wait',
    stage: 'review',
    doing: 'Queued for review',
  },
  review: { label: 'Review', kind: 'node', stage: 'review', doing: 'Reviewing the pull request' },
  your_review: {
    label: 'Your review',
    kind: 'node',
    stage: 'merge',
    doing: 'Waiting for your review',
  },
  merge: { label: 'Merge', kind: 'node', stage: 'merge', doing: 'Merging the pull request' },
  done: { label: 'Done', kind: 'node', stage: 'merge', doing: 'Done' },
};

/** `mark_ready` → `Mark ready`. */
export function sentenceCase(token: string): string {
  const words = token.replaceAll('_', ' ').trim();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/**
 * The copy for a step token. A token this dashboard has no copy for is a node in `fallbackStage`
 * (the previous step's stage), labelled from the token, so the step is never dropped.
 */
export function stepCopy(token: string, fallbackStage: RailStage): StepCopy {
  if (Object.hasOwn(STEPS, token)) {
    return STEPS[token];
  }
  const label = sentenceCase(token);
  return { label, kind: 'node', stage: fallbackStage, doing: label };
}
