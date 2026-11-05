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
  /** Whether Foundry rebases the worker branch onto the default branch after each run. */
  rebaseAfterRun: boolean;
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

/** The provider a repository webhook is registered on. */
export type WebhookProvider = 'github' | 'gitlab';

/** Manual registration details. Never carries the secret — see {@link RepositoryWebhookSecret}. */
export interface RepositoryWebhookInfo {
  status: WebhookStatus;
  payloadUrl: string | null;
  /** GitHub only — GitLab always sends JSON. */
  contentType: string;
  /** GitHub event names, or GitLab trigger labels. */
  events: string[];
  backstopIntervalMinutes: number;
  provider: WebhookProvider;
}

export interface RepositoryWebhookSecret {
  secret: string;
}

export type WebhookRegistrationOutcomeKind = 'created' | 'updated' | 'skipped' | 'failed';

export type WebhookRegistrationOutcomeReason =
  | 'not-configured'
  | 'no-server-token'
  | 'server-token-unreadable'
  | 'insufficient-permission'
  | 'provider-error';

/** Per-request result of registering a webhook with the account's server-side token. Never persisted. */
export interface WebhookRegistrationOutcome {
  kind: WebhookRegistrationOutcomeKind;
  /** Present only on skipped / failed. */
  reason?: WebhookRegistrationOutcomeReason | null;
  /** Human-readable explanation; present only on failed. */
  detail?: string | null;
}

/** Response of the repository page's "Register webhook" action. */
export interface RegisterWebhookResponse {
  webhook: RepositoryWebhookInfo;
  outcome: WebhookRegistrationOutcome;
}

/** Whether a registration outcome left the hook registered on the provider. */
export function isWebhookRegistered(outcome: WebhookRegistrationOutcome): boolean {
  return outcome.kind === 'created' || outcome.kind === 'updated';
}

/** Operator-facing message for a registration outcome. */
export function webhookRegistrationMessage(outcome: WebhookRegistrationOutcome, provider: WebhookProvider): string {
  const providerName = provider === 'gitlab' ? 'GitLab' : 'GitHub';
  const permission = provider === 'gitlab'
    ? 'the Maintainer role'
    : 'the Webhooks (read and write) permission';

  switch (outcome.kind) {
    case 'created':
      return `Webhook created on ${providerName}. It becomes active on its first delivery.`;
    case 'updated':
      return `The existing webhook on ${providerName} was updated. It becomes active on its next delivery.`;
    case 'skipped':
      switch (outcome.reason) {
        case 'no-server-token':
          return `This account has no server-side token, so Foundry can't register the webhook. Add one with ${permission} in the account settings, or add the webhook manually.`;
        case 'server-token-unreadable':
          return "This account's server-side token can't be read. Re-enter it in the account settings, or add the webhook manually.";
        default:
          return 'Webhooks are off, so nothing was registered.';
      }
    case 'failed':
      if (outcome.reason === 'insufficient-permission') {
        return `The server-side token needs ${permission} to manage webhooks. Grant it, or add the webhook manually.`;
      }
      return outcome.detail
        ? `${providerName} rejected the registration: ${outcome.detail}`
        : `${providerName} rejected the registration.`;
  }
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
  /** Whether to rebase the worker branch onto the default branch after each run. Server default: true. */
  rebaseAfterRun?: boolean;
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
  /** Outcome of registering the webhook with the server-side token. Absent from older servers. */
  webhook?: WebhookRegistrationOutcome | null;
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
  /** Omitted leaves the setting unchanged. */
  rebaseAfterRun?: boolean;
}
