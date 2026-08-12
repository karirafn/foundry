export type FailureCategoryToken =
  | 'non_zero_exit'
  | 'timed_out'
  | 'container_error'
  | 'provider_error'
  | 'usage_limited'
  | 'worker_bootstrap_failed'
  | 'credits_exhausted';

interface FailureCategoryDisplay {
  readonly label: string;
  readonly cssClass: string;
}

export const FAILURE_CATEGORY_DISPLAY = {
  non_zero_exit: { label: 'NON-ZERO EXIT', cssClass: 'badge--failure-non-zero-exit' },
  timed_out: { label: 'TIMED OUT', cssClass: 'badge--failure-timed-out' },
  container_error: { label: 'CONTAINER ERROR', cssClass: 'badge--failure-container-error' },
  provider_error: { label: 'PROVIDER ERROR', cssClass: 'badge--failure-provider-error' },
  usage_limited: { label: 'USAGE LIMITED', cssClass: 'badge--usage-limited' },
  worker_bootstrap_failed: { label: 'BOOTSTRAP FAILED', cssClass: 'badge--failure-bootstrap' },
  credits_exhausted: { label: 'CREDITS EXHAUSTED', cssClass: 'badge--failure-credits-exhausted' },
} as const satisfies Record<FailureCategoryToken, FailureCategoryDisplay>;

export function getFailureCategoryDisplay(token: string): FailureCategoryDisplay | null {
  if (Object.prototype.hasOwnProperty.call(FAILURE_CATEGORY_DISPLAY, token)) {
    return FAILURE_CATEGORY_DISPLAY[token as FailureCategoryToken];
  }
  return null;
}
