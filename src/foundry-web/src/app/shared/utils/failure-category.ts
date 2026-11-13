export type FailureCategoryToken =
  | 'non_zero_exit'
  | 'timed_out'
  | 'container_error'
  | 'provider_error'
  | 'provider_unreachable'
  | 'usage_limited'
  | 'worker_bootstrap_failed'
  | 'credits_exhausted'
  | 'model_unavailable'
  | 'turn_limit'
  | 'auth_invalid'
  | 'transient_api_error'
  | 'pr_closed'
  | 'cancelled'
  | 'verdict_invalid'
  | 'publish_failed'
  | 'publish_expired'
  | 'run_discarded';

interface FailureCategoryDisplay {
  readonly label: string;
  readonly cssClass: string;
}

export const FAILURE_CATEGORY_DISPLAY = {
  non_zero_exit: { label: 'NON-ZERO EXIT', cssClass: 'badge--failure-non-zero-exit' },
  timed_out: { label: 'TIMED OUT', cssClass: 'badge--failure-timed-out' },
  container_error: { label: 'CONTAINER ERROR', cssClass: 'badge--failure-container-error' },
  provider_error: { label: 'PROVIDER ERROR', cssClass: 'badge--failure-provider-error' },
  provider_unreachable: { label: 'PROVIDER UNREACHABLE', cssClass: 'badge--failure-provider-unreachable' },
  usage_limited: { label: 'USAGE LIMITED', cssClass: 'badge--usage-limited' },
  worker_bootstrap_failed: { label: 'BOOTSTRAP FAILED', cssClass: 'badge--failure-bootstrap' },
  credits_exhausted: { label: 'CREDITS EXHAUSTED', cssClass: 'badge--failure-credits-exhausted' },
  model_unavailable: { label: 'MODEL UNAVAILABLE', cssClass: 'badge--failure-model-unavailable' },
  turn_limit: { label: 'TURN LIMIT', cssClass: 'badge--failure-turn-limit' },
  auth_invalid: { label: 'AUTH FAILED', cssClass: 'badge--failure-auth-invalid' },
  transient_api_error: { label: 'CLAUDE API ERROR', cssClass: 'badge--failure-transient-api-error' },
  pr_closed: { label: 'PR CLOSED', cssClass: 'badge--failure-pr-closed' },
  cancelled: { label: 'CANCELLED', cssClass: 'badge--failure-cancelled' },
  verdict_invalid: { label: 'INVALID VERDICT', cssClass: 'badge--failure-verdict-invalid' },
  publish_failed: { label: 'PUBLISH FAILED', cssClass: 'badge--failure-publish-failed' },
  publish_expired: { label: 'PUBLISH EXPIRED', cssClass: 'badge--failure-publish-expired' },
  run_discarded: { label: 'RUN DISCARDED', cssClass: 'badge--failure-cancelled' },
} as const satisfies Record<FailureCategoryToken, FailureCategoryDisplay>;

export function getFailureCategoryDisplay(token: string): FailureCategoryDisplay | null {
  if (Object.prototype.hasOwnProperty.call(FAILURE_CATEGORY_DISPLAY, token)) {
    return FAILURE_CATEGORY_DISPLAY[token as FailureCategoryToken];
  }
  return null;
}
