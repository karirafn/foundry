export type EligibilityStatus = 'eligible' | 'ineligible' | 'unreachable';

export type EligibilityReason = 'never-probed' | 'rate-limited' | 'branch-rules-unavailable';

export function eligibilityStatusLabel(status: EligibilityStatus): string {
  switch (status) {
    case 'eligible':
      return 'Eligible';
    case 'ineligible':
      return 'Ineligible';
    case 'unreachable':
      return 'Unverified';
  }
}

export interface EligibilityViolation {
  rule: string;
  description: string;
}

export interface RepositoryEligibility {
  status: EligibilityStatus;
  violations: EligibilityViolation[];
  reason: EligibilityReason | null;
}

export interface RepositorySummary {
  id: string;
  slug: string;
  accountId: string;
  accountName: string;
  providerType: string;
  position: number;
  pollIntervalSeconds: number | null;
  effectivePollIntervalSeconds: number;
  pollIntervalIsDefault: boolean;
  isActive: boolean;
  maxConcurrentWorkers: number;
  lastPolledAt: string | null;
  eligibility: RepositoryEligibility | null;
  /** Effective webhook health (ADR 0085). Absent on responses from older servers. */
  webhookStatus?: WebhookStatus;
}

/** Per-repository webhook health. Only `active` polls at the Backstop Interval. */
export type WebhookStatus = 'not-configured' | 'not-registered' | 'pending' | 'active' | 'silent';

export function webhookStatusLabel(status: WebhookStatus): string {
  switch (status) {
    case 'not-configured':
      return 'Not configured';
    case 'not-registered':
      return 'Not registered';
    case 'pending':
      return 'Waiting for first delivery';
    case 'active':
      return 'Active';
    case 'silent':
      return 'Silent';
  }
}

/** Manual registration details. Never carries the secret — see {@link RepositoryWebhookSecret}. */
export interface RepositoryWebhookInfo {
  status: WebhookStatus;
  payloadUrl: string | null;
  contentType: string;
  events: string[];
  backstopIntervalMinutes: number;
}

export interface RepositoryWebhookSecret {
  secret: string;
}

export interface AvailableRepository {
  slug: string;
  isPrivate: boolean;
  canPush: boolean;
  isMonitored: boolean;
}

export interface AvailableRepositoriesResponse {
  hasClaims: boolean;
  repositories: AvailableRepository[];
  noPushAccessExplanation: string;
}

export interface CreateRepositoryRequest {
  slug: string;
  pollIntervalSeconds: number | null;
  maxConcurrentWorkers: number | null;
  /** Whether Foundry may write the default-branch rules eligibility requires. Server default: true. */
  applyBranchRules?: boolean;
}

export type BranchRulesOutcomeKind = 'applied' | 'already-present' | 'not-requested' | 'skipped' | 'failed';

export type BranchRulesOutcomeReason =
  | 'access-failure'
  | 'unverifiable'
  | 'no-rules-token'
  | 'insufficient-permission'
  | 'provider-error';

/** Per-request result of a branch-rules apply attempt. Returned on create, never persisted. */
export interface BranchRulesOutcome {
  kind: BranchRulesOutcomeKind;
  /** Present only on skipped / failed. */
  reason?: BranchRulesOutcomeReason | null;
  /** Provider rules settings page; present on every kind except applied. */
  settingsUrl?: string | null;
  /** Human-readable explanation; present only on failed. */
  detail?: string | null;
}

export interface CreateRepositoryResponse {
  repository: RepositorySummary;
  branchRules: BranchRulesOutcome;
}

/** Response of the repository card's "Apply branch rules" action — same contract as create. */
export type ApplyBranchRulesResponse = CreateRepositoryResponse;

const BRANCH_PROTECTION_RULE_PREFIX = 'branch-protection:';

/**
 * True when the repository is ineligible solely through `branch-protection:*` violations — the
 * only state the "Apply branch rules" action can fix.
 */
export function canApplyBranchRules(eligibility: RepositoryEligibility | null): boolean {
  return eligibility !== null
    && eligibility.status === 'ineligible'
    && eligibility.violations.length > 0
    && eligibility.violations.every(v => v.rule.startsWith(BRANCH_PROTECTION_RULE_PREFIX));
}

export interface UpdateRepositoryRequest {
  pollIntervalSeconds: number | null;
  isActive: boolean;
  maxConcurrentWorkers: number;
}
