// Human-readable labels for the post-run rebase step (rebaseOutcome / rebaseSkippedReason).
// Unknown values fall back to the raw wire text so a newer backend never renders blank.

const REBASE_SKIPPED_REASON_LABELS: Record<string, string> = {
  timeout: 'timeout',
  'provider-error': 'provider error',
  unsupported: 'unsupported',
  'permission-denied': 'permission denied',
  'force-push-protected': 'force-push protected',
  'rebase-failed': 'rebase failed',
  'not-found': 'not found',
  'stale-head': 'stale head',
  'unrecognised-error': 'unrecognised error',
};

const REBASE_OUTCOME_LABELS: Record<string, string> = {
  up_to_date: 'Up to date',
  rebased: 'Rebased',
  disabled: 'Disabled',
  skipped: 'Skipped',
  conflict: 'Conflict',
};

export function formatRebaseSkippedReason(reason: string | null | undefined): string | null {
  if (reason == null || reason === '') {
    return null;
  }
  return REBASE_SKIPPED_REASON_LABELS[reason] ?? reason;
}

/** Detail-row label for every rebase outcome, e.g. "Skipped (timeout)". Null when no outcome. */
export function rebaseOutcomeLabel(
  outcome: string | null | undefined,
  skippedReason?: string | null,
): string | null {
  if (outcome == null || outcome === '') {
    return null;
  }
  const label = REBASE_OUTCOME_LABELS[outcome] ?? outcome;
  if (outcome === 'skipped') {
    const reason = formatRebaseSkippedReason(skippedReason);
    return reason !== null ? `${label} (${reason})` : label;
  }
  return label;
}

export interface RebaseOutcomeBadge {
  readonly label: string;
  readonly cssClass: string;
}

/** Card badge — only the outcomes an operator should notice (skipped, conflict). */
export function rebaseOutcomeBadge(
  outcome: string | null | undefined,
  skippedReason?: string | null,
): RebaseOutcomeBadge | null {
  if (outcome === 'conflict') {
    return { label: 'Rebase conflict', cssClass: 'issue-card__checks-badge--error' };
  }
  if (outcome === 'skipped') {
    const reason = formatRebaseSkippedReason(skippedReason);
    return {
      label: reason !== null ? `Rebase skipped: ${reason}` : 'Rebase skipped',
      cssClass: 'issue-card__checks-badge--warning',
    };
  }
  return null;
}
