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
  /** Whether Foundry merges the pull request it opened once every check passes (ADR 0105). */
  mergeWhenChecksPass: boolean;
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
      return 'No webhook URL';
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
  | 'token-unreadable'
  | 'insufficient-permission'
  | 'provider-error';

/** Per-request result of registering a webhook with the account token. Never persisted. */
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
        case 'token-unreadable':
          return "The account token can't be read. Re-enter it on the account, or add the webhook manually.";
        default:
          return 'Foundry has no webhook URL yet, so nothing was registered. Set it in Dispatch settings.';
      }
    case 'failed':
      if (outcome.reason === 'insufficient-permission') {
        return `The account token needs ${permission} to manage webhooks. Give the token that permission, or add the webhook manually.`;
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
  /** Whether Foundry merges the pull request it opened once every check passes. Server default: false. */
  mergeWhenChecksPass?: boolean;
}

export type BranchRulesOutcomeKind = 'applied' | 'already-present' | 'not-requested' | 'skipped' | 'failed';

export type BranchRulesOutcomeReason =
  | 'access-failure'
  | 'unverifiable'
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
  /** Outcome of registering the webhook with the account token. Absent from older servers. */
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
  /** Whether Foundry merges the pull request it opened once every check passes. Server default: false. Omitted leaves the setting unchanged. */
  mergeWhenChecksPass?: boolean;
}

export type RepositoryStatusTone = 'ineligible' | 'warning' | 'neutral';

export type RepositoryStatusTag =
  | { kind: 'ineligible'; label: 'Ineligible'; tone: 'ineligible' }
  | { kind: 'unverified'; label: 'Unverified'; tone: 'warning' }
  | { kind: 'webhook-silent'; label: 'Webhook silent'; tone: 'warning' }
  | { kind: 'webhook-pending'; label: 'Webhook pending'; tone: 'neutral' }
  | { kind: 'no-webhook'; label: 'No webhook'; tone: 'neutral' };

const INELIGIBLE_TAG: RepositoryStatusTag = { kind: 'ineligible', label: 'Ineligible', tone: 'ineligible' };
const UNVERIFIED_TAG: RepositoryStatusTag = { kind: 'unverified', label: 'Unverified', tone: 'warning' };
const WEBHOOK_SILENT_TAG: RepositoryStatusTag = { kind: 'webhook-silent', label: 'Webhook silent', tone: 'warning' };
const WEBHOOK_PENDING_TAG: RepositoryStatusTag = { kind: 'webhook-pending', label: 'Webhook pending', tone: 'neutral' };
const NO_WEBHOOK_TAG: RepositoryStatusTag = { kind: 'no-webhook', label: 'No webhook', tone: 'neutral' };

/** The one status tag a repository row shows, or null when nothing needs attention. */
export function repositoryStatusTag(
  repo: Pick<RepositorySummary, 'eligibility' | 'isActive' | 'webhookStatus'>,
): RepositoryStatusTag | null {
  if (repo.eligibility?.status === 'ineligible') {
    return INELIGIBLE_TAG;
  }
  if (repo.eligibility?.status === 'unreachable') {
    return UNVERIFIED_TAG;
  }
  // A paused repository is not polled, so its webhook health is not actionable.
  if (!repo.isActive) {
    return null;
  }
  switch (repo.webhookStatus) {
    case 'silent':
      return WEBHOOK_SILENT_TAG;
    case 'pending':
      return WEBHOOK_PENDING_TAG;
    case 'not-registered':
      return NO_WEBHOOK_TAG;
    default:
      return null;
  }
}

/** Splits a slug into its namespace (trailing `/` included, `''` when none) and its name. */
export function splitRepositorySlug(slug: string): { namespace: string; name: string } {
  const nameStart = slug.lastIndexOf('/') + 1;
  return { namespace: slug.slice(0, nameStart), name: slug.slice(nameStart) };
}

/** 30 → "30s", 300 → "5m". */
export function formatPollInterval(seconds: number): string {
  return seconds < 60 ? `${seconds}s` : `${Math.round(seconds / 60)}m`;
}

function lastPolledPhrase(lastPolledAt: string | null, now: number): string {
  if (lastPolledAt === null) {
    return 'never polled';
  }
  const minutes = Math.floor((now - new Date(lastPolledAt).getTime()) / 60_000);
  if (minutes < 1) {
    return 'last polled just now';
  }
  if (minutes < 60) {
    return `last polled ${minutes}m ago`;
  }
  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return `last polled ${hours}h ago`;
  }
  return `last polled ${Math.floor(hours / 24)}d ago`;
}

/** How the repository receives issue updates, as one muted line. */
export function repositoryUpdatesLine(repo: RepositorySummary, now: number): string {
  if (!repo.isActive) {
    return `Paused, ${lastPolledPhrase(repo.lastPolledAt, now)}`;
  }
  // Webhook passes do not stamp lastPolledAt, so a time here would mislead.
  if (repo.webhookStatus === 'active') {
    return 'Updates by webhook';
  }
  return `Polls every ${formatPollInterval(repo.effectivePollIntervalSeconds)}, ${lastPolledPhrase(repo.lastPolledAt, now)}`;
}

/** Muted tags for the settings that differ from their defaults. */
export function repositorySettingTags(repo: RepositorySummary): string[] {
  const tags: string[] = [];
  if (repo.maxConcurrentWorkers !== 1) {
    tags.push(`${repo.maxConcurrentWorkers} workers`);
  }
  if (repo.mergeWhenChecksPass) {
    tags.push('Merges on pass');
  }
  if (!repo.rebaseAfterRun) {
    tags.push('No rebase');
  }
  return tags;
}
