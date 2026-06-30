export type FailureCategoryToken =
  | 'non_zero_exit'
  | 'timed_out'
  | 'container_error'
  | 'usage_limited'
  | 'worker_bootstrap_failed';

interface FailureCategoryDisplay {
  readonly label: string;
  readonly cssClass: string;
}

export const FAILURE_CATEGORY_DISPLAY = {
  non_zero_exit: { label: 'NON-ZERO EXIT', cssClass: 'badge--failure-non-zero-exit' },
  timed_out: { label: 'TIMED OUT', cssClass: 'badge--failure-timed-out' },
  container_error: { label: 'CONTAINER ERROR', cssClass: 'badge--failure-container-error' },
  usage_limited: { label: 'USAGE LIMITED', cssClass: 'badge--usage-limited' },
  worker_bootstrap_failed: { label: 'BOOTSTRAP FAILED', cssClass: 'badge--failure-bootstrap' },
} as const satisfies Record<FailureCategoryToken, FailureCategoryDisplay>;

export function getFailureCategoryDisplay(token: string): FailureCategoryDisplay | null {
  if (Object.prototype.hasOwnProperty.call(FAILURE_CATEGORY_DISPLAY, token)) {
    return FAILURE_CATEGORY_DISPLAY[token as FailureCategoryToken];
  }
  return null;
}
