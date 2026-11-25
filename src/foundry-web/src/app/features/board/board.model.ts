import { IssueStage, IssueSummary, RunKind } from '../issues/issue.model';
import { formatMergeSkippedReason } from '../issues/merge-outcome.format';
import { formatReviewSkippedReason } from '../issues/review-outcome.format';
import { RepositorySummary } from '../settings/repositories/repository.model';
import { getFailureCategoryDisplay } from '../../shared/utils/failure-category';
import { WORKING_STATES } from '../../shared/utils/issue-state';
import { checksUrl, providerDisplayName, providerTerminology } from '../../shared/utils/provider.util';

/** The stages the board shows; a completed (`done`) issue leaves the board. */
export type BoardStage = Exclude<IssueStage, 'done'>;

/** Stage names, in flow order; the stage chip marks a card's position among them. */
export const STAGE_LABELS: Readonly<Record<BoardStage, string>> = {
  triage: 'Triage',
  implement: 'Implement',
  review: 'Review',
  merge: 'Merge',
};

export const STAGE_ORDER: readonly BoardStage[] = ['triage', 'implement', 'review', 'merge'];

/** The label that starts work on an issue; the backend constant `WebhookRelevance.TriggerLabel`. */
export const TRIGGER_LABEL = 'foundry';

export type ReasonTone = 'error' | 'warning' | 'ready' | 'neutral';

export interface NeedsReason {
  readonly label: string;
  readonly tone: ReasonTone;
}

/** The bands, in the order the page shows them; `workers` and `steps` together are In progress. */
export type BoardBand = 'needs' | 'workers' | 'steps' | 'pipeline' | 'blocked';

interface ItemBase {
  readonly issue: IssueSummary;
  /** Null when the server sent a stage this dashboard does not know; the row is still shown. */
  readonly stage: BoardStage | null;
}

export interface NeedsItem extends ItemBase {
  readonly band: 'needs';
  readonly reason: NeedsReason;
  /** One line on what the person is needed for. */
  readonly explanation: string;
}

export interface WorkerItem extends ItemBase {
  readonly band: 'workers';
  readonly runKind: RunKind | null;
}

/** In progress with no worker running: a step Foundry's own code runs, or facts no other rule places. */
export interface StepItem extends ItemBase {
  readonly band: 'steps';
}

/**
 * Where a pipeline row stands: a dispatch position, no position yet (detected, eligible), or held
 * by a repository that can't be worked on (ineligible or unreachable).
 */
export type PipelineSlot =
  | { readonly kind: 'position'; readonly position: number }
  | { readonly kind: 'detected' }
  | { readonly kind: 'ineligible' };

export interface PipelineItem extends ItemBase {
  readonly band: 'pipeline';
  readonly runKind: RunKind | null;
  readonly slot: PipelineSlot;
}

export interface BlockedItem extends ItemBase {
  readonly band: 'blocked';
  readonly runKind: RunKind | null;
}

export type BoardItem = NeedsItem | WorkerItem | StepItem | PipelineItem | BlockedItem;

/** One repository that holds queued issues because it can't be worked on. */
export interface RepoAlert {
  readonly repositoryId: string;
  readonly slug: string;
  readonly heldCount: number;
  /** The first violation's server-written description; null when the repository list lacks it. */
  readonly firstViolation: string | null;
  /** How many violations follow the first. */
  readonly moreViolations: number;
}

export interface BoardBands {
  readonly alerts: readonly RepoAlert[];
  readonly needs: readonly NeedsItem[];
  readonly workers: readonly WorkerItem[];
  readonly steps: readonly StepItem[];
  readonly pipeline: readonly PipelineItem[];
  readonly blocked: readonly BlockedItem[];
}

/** The lists that hold issue items, keyed by band. */
export const ITEM_BANDS: readonly BoardBand[] = [
  'needs',
  'workers',
  'steps',
  'pipeline',
  'blocked',
];

/** Which needs reason applies, so the row's action follows it. */
type NeedsKey =
  | 'needs_answers'
  | 'no_changes'
  | 'failed'
  | 'triage_failed'
  | 'review_failed'
  | 'publish_failed'
  | 'publish_blocked'
  | 'branch_diverged'
  | 'needs_you'
  | 'checks_red'
  | 'infra_failed'
  | 'review_skipped'
  | 'merge_skipped'
  | 'pr_ready';

interface ReasonCopy extends NeedsReason {
  readonly key: NeedsKey;
  readonly explanation: (issue: IssueSummary) => string;
}

function failedRole(issue: IssueSummary): string {
  if (issue.state === 'triage_failed' || issue.stage === 'triage') {
    return 'Triage';
  }
  return issue.state === 'review_failed' || issue.stage === 'review' ? 'Review' : 'Implement';
}

function failedExplanation(issue: IssueSummary): string {
  const role = failedRole(issue);
  const classification = issue.failureClassification;
  if (!classification) {
    return 'The run failed';
  }
  const label =
    getFailureCategoryDisplay(classification)?.label ?? classification.replaceAll('_', ' ');
  return `${role} run failed: ${label.toLowerCase()}`;
}

const FAILED: ReasonCopy = { key: 'failed', label: 'Failed', tone: 'error', explanation: failedExplanation };

const STATE_REASONS: Readonly<Partial<Record<IssueSummary['state'], ReasonCopy>>> = {
  awaiting_input: {
    key: 'needs_answers',
    label: 'Needs answers',
    tone: 'warning',
    explanation: (issue) =>
      issue.questionPostStatus === 'pending' ? 'Posting questions…' : 'Questions are waiting for your answers',
  },
  unchanged: { key: 'no_changes', label: 'No changes', tone: 'neutral', explanation: () => 'The run made no changes' },
  failed: FAILED,
  continuable_failed: FAILED,
  revision_failed: FAILED,
  triage_failed: { ...FAILED, key: 'triage_failed' },
  review_failed: { key: 'review_failed', label: 'Review failed', tone: 'warning', explanation: failedExplanation },
  publish_failed: {
    key: 'publish_failed',
    label: 'Publish failed',
    tone: 'error',
    explanation: () => "Foundry couldn't push the run's commits",
  },
  publish_blocked: {
    key: 'publish_blocked',
    label: 'Publish blocked',
    tone: 'error',
    explanation: () => 'A secret was found, so nothing was pushed',
  },
  branch_diverged: {
    key: 'branch_diverged',
    label: 'Branch diverged',
    tone: 'warning',
    explanation: () => "The branch moved; the run's work is on a side branch",
  },
};

// A state the server marks as needing action that this table doesn't name yet still reads as such.
const NEEDS_YOU: ReasonCopy = {
  key: 'needs_you',
  label: 'Needs you',
  tone: 'neutral',
  explanation: () => "Open to see what's needed",
};

const CHECKS_RED: ReasonCopy = {
  key: 'checks_red',
  label: 'Checks red',
  tone: 'error',
  explanation: () => 'Checks failed',
};

// Warning, not error: no job ran the code, so nothing is known to be wrong with it; the person retries CI.
const INFRA_FAILED: ReasonCopy = {
  key: 'infra_failed',
  label: "CI couldn't run",
  tone: 'warning',
  explanation: (issue) =>
    `CI failed before running any code; retry it on ${providerDisplayName(issue.providerType ?? '') || 'the provider'}`,
};

function mergeSkippedExplanation(issue: IssueSummary): string {
  const reason = formatMergeSkippedReason(issue.mergeSkippedReason);
  return reason !== null
    ? `Checks passed; Foundry didn't merge: ${reason}`
    : "Checks passed; Foundry didn't merge";
}

function reviewSkippedExplanation(issue: IssueSummary): string {
  const reason = formatReviewSkippedReason(issue.reviewSkippedReason);
  return reason !== null
    ? `Checks passed; the review didn't finish: ${reason}`
    : "Checks passed; the review didn't finish";
}

// Warning: the reviewer never judged the pull request, so a person reviews it unaided or retries.
const REVIEW_SKIPPED: ReasonCopy = {
  key: 'review_skipped',
  label: 'Review skipped',
  tone: 'warning',
  explanation: reviewSkippedExplanation,
};

const MERGE_SKIPPED: ReasonCopy = {
  key: 'merge_skipped',
  label: 'Merge skipped',
  tone: 'warning',
  explanation: mergeSkippedExplanation,
};

function reasonCopy(issue: IssueSummary): ReasonCopy {
  if (issue.state === 'review') {
    if (issue.checksOutcome === 'still_failing') {
      return CHECKS_RED;
    }
    if (issue.checksOutcome === 'infrastructure_failed') {
      return INFRA_FAILED;
    }
    if (issue.reviewOutcome === 'skipped') {
      return REVIEW_SKIPPED;
    }
    if (issue.mergeOutcome === 'skipped') {
      return MERGE_SKIPPED;
    }
    return {
      key: 'pr_ready',
      label: `${providerTerminology(issue.providerType ?? 'github').prAbbrev} ready`,
      tone: 'ready',
      explanation: () => 'Ready for your review',
    };
  }
  return STATE_REASONS[issue.state] ?? NEEDS_YOU;
}

/**
 * A review whose checks are not red: its work is finished and a person only reviews or merges, so
 * Needs you shows it as a one-line row rather than a full card.
 */
export function isCompactReview(issue: IssueSummary): boolean {
  return (
    issue.state === 'review' &&
    issue.checksOutcome !== 'still_failing' &&
    issue.checksOutcome !== 'infrastructure_failed'
  );
}

/** A link that opens the pull request for review in a new tab; `hint` is its screen-reader suffix. */
export interface ReviewLink {
  readonly href: string;
  readonly label: string;
  readonly hint: string;
}

/** The review link of an issue that awaits a person's review, or null when it has none. */
export function reviewableLink(issue: IssueSummary): ReviewLink | null {
  if (!isCompactReview(issue) || !issue.pullRequestUrl) {
    return null;
  }
  const provider = issue.providerType ?? 'github';
  return {
    href: issue.pullRequestUrl,
    label: `Review ${providerTerminology(provider).prAbbrev}`,
    hint: ` (opens ${providerDisplayName(provider)} in a new tab)`,
  };
}

/** Why the issue needs a person, or null when it doesn't. */
export function needsReason(issue: IssueSummary): NeedsReason | null {
  if (!issue.needsAction) {
    return null;
  }
  const { label, tone } = reasonCopy(issue);
  return { label, tone };
}

export function isBoardStage(stage: IssueStage | null): stage is BoardStage {
  return stage !== null && stage !== 'done';
}

/** A worker is running for the issue (not Foundry publishing its commits). */
export function isRunning(issue: IssueSummary): boolean {
  return WORKING_STATES.has(issue.state) && issue.publishPhase == null;
}

/**
 * The band an issue belongs in, from the server's facts only; the first matching rule wins. Facts no
 * rule matches still show in In progress, so live work never disappears from the board.
 */
export function bandFor(issue: IssueSummary): BoardBand {
  // A failed review run waits for its automatic retry with no worker, step or queue slot, so it
  // shows with the failures, as every other auto-retryable failed state does, rather than vanish.
  if (issue.needsAction || issue.state === 'review_failed') {
    return 'needs';
  }
  if (isRunning(issue)) {
    return 'workers';
  }
  if (issue.waitsOnBlockers) {
    return 'blocked';
  }
  if (issue.awaitsDispatch) {
    return 'pipeline';
  }
  return 'steps';
}

function isHeldByRepository(issue: IssueSummary): boolean {
  return (
    issue.repositoryEligibilityStatus === 'ineligible' ||
    issue.repositoryEligibilityStatus === 'unreachable'
  );
}

const TONE_RANK: Readonly<Record<ReasonTone, number>> = {
  error: 0,
  warning: 1,
  ready: 2,
  neutral: 3,
};

function since(timestamp: string): number {
  const ms = Date.parse(timestamp);
  return Number.isNaN(ms) ? Number.POSITIVE_INFINITY : ms;
}

/** Longest waiting first, then by issue number. */
export function compareWaiting(a: NeedsItem, b: NeedsItem): number {
  return (
    since(a.issue.enteredStateAt) - since(b.issue.enteredStateAt) ||
    a.issue.issueNumber - b.issue.issueNumber
  );
}

// Tone first, so a failure never sits below ready PRs; then longest waiting.
function compareNeeds(a: NeedsItem, b: NeedsItem): number {
  return TONE_RANK[a.reason.tone] - TONE_RANK[b.reason.tone] || compareWaiting(a, b);
}

function compareRunning(a: WorkerItem, b: WorkerItem): number {
  return since(a.issue.enteredStateAt) - since(b.issue.enteredStateAt);
}

// Pipeline order (ADR 0067): dispatch positions, then rows with no position yet, then rows held by
// their repository; each group keeps the server's array order (sort is stable).
const SLOT_RANK: Readonly<Record<PipelineSlot['kind'], number>> = {
  position: 0,
  detected: 1,
  ineligible: 2,
};

function comparePipeline(a: PipelineItem, b: PipelineItem): number {
  const rank = SLOT_RANK[a.slot.kind] - SLOT_RANK[b.slot.kind];
  if (rank !== 0 || a.slot.kind !== 'position' || b.slot.kind !== 'position') {
    return rank;
  }
  return a.slot.position - b.slot.position;
}

function pipelineSlot(issue: IssueSummary, positions: ReadonlyMap<string, number>): PipelineSlot {
  if (isHeldByRepository(issue)) {
    return { kind: 'ineligible' };
  }
  const position = positions.get(issue.id);
  return position === undefined ? { kind: 'detected' } : { kind: 'position', position };
}

/** One alert per repository whose eligibility is `ineligible` and that holds queued issues. */
function buildAlerts(
  pipeline: readonly PipelineItem[],
  repositories: readonly RepositorySummary[],
): RepoAlert[] {
  const held = new Map<string, PipelineItem[]>();
  for (const item of pipeline) {
    if (item.issue.repositoryEligibilityStatus !== 'ineligible') {
      continue;
    }
    const items = held.get(item.issue.repositoryId) ?? [];
    items.push(item);
    held.set(item.issue.repositoryId, items);
  }
  return [...held].map(([repositoryId, items]) => {
    // Joined by id: slugs are not unique across accounts on the same provider.
    const repository = repositories.find((r) => r.id === repositoryId);
    const violations = repository?.eligibility?.violations ?? [];
    return {
      repositoryId,
      slug: repository?.slug ?? items[0].issue.repositorySlug,
      heldCount: items.length,
      firstViolation: violations[0]?.description ?? null,
      moreViolations: Math.max(0, violations.length - 1),
    };
  });
}

/**
 * Places the issues in bands. `dispatchQueueIds` is the dispatchable queue in dispatch order; a
 * row's position is its index there. `repositories` supplies the repository alerts' violations.
 */
export function buildBoardBands(
  issues: readonly IssueSummary[],
  dispatchQueueIds: readonly string[],
  repositories: readonly RepositorySummary[],
): BoardBands {
  const positions = new Map<string, number>(dispatchQueueIds.map((id, i) => [id, i + 1]));
  const needs: NeedsItem[] = [];
  const workers: WorkerItem[] = [];
  const steps: StepItem[] = [];
  const pipeline: PipelineItem[] = [];
  const blocked: BlockedItem[] = [];

  for (const issue of issues) {
    const stage = issue.stage;
    if (stage === 'done') {
      continue;
    }
    const band = bandFor(issue);
    switch (band) {
      case 'needs': {
        const { label, tone, explanation } = reasonCopy(issue);
        needs.push({
          band,
          issue,
          stage,
          reason: { label, tone },
          explanation: explanation(issue),
        });
        break;
      }
      case 'workers':
        workers.push({ band, issue, stage, runKind: issue.runKind });
        break;
      case 'steps':
        steps.push({ band, issue, stage });
        break;
      case 'blocked':
        blocked.push({ band, issue, stage, runKind: issue.runKind });
        break;
      case 'pipeline':
        pipeline.push({
          band,
          issue,
          stage,
          runKind: issue.runKind,
          slot: pipelineSlot(issue, positions),
        });
        break;
    }
  }

  const orderedPipeline = pipeline.sort(comparePipeline);
  return {
    alerts: buildAlerts(orderedPipeline, repositories),
    needs: needs.sort(compareNeeds),
    workers: workers.sort(compareRunning),
    steps,
    pipeline: orderedPipeline,
    blocked,
  };
}

/** The last segment of a repository slug: `acme/isal-monitoring` → `isal-monitoring`. */
export function repoName(slug: string): string {
  const segments = slug.split('/');
  return segments[segments.length - 1] || slug;
}

/** A card or row asked to open the issue pane; `invoker` takes focus back when it closes. */
export interface CardOpened {
  readonly id: string;
  readonly invoker: HTMLElement;
}

/** The actions a Needs you row runs in place; a subset of what the pane offers. */
export type RowActionKind = 'retry_issue' | 'retry_triage' | 'retry_review' | 'retry_publish' | 'retry_post';

/**
 * The one primary control a Needs you row carries: a provider link opened in a new tab, an action
 * run through IssueActionsService, or `open`, which opens the pane for actions that need its context.
 * `ariaLabel` starts with the visible label.
 */
export type RowActionSpec =
  | { readonly kind: 'link'; readonly label: string; readonly ariaLabel: string; readonly href: string }
  | {
      readonly kind: 'action';
      readonly label: string;
      readonly pendingLabel: string;
      readonly ariaLabel: string;
      readonly action: RowActionKind;
    }
  | { readonly kind: 'open'; readonly label: string; readonly ariaLabel: string };

function providerLink(issue: IssueSummary, label: string, purpose: string, href: string): RowActionSpec {
  const provider = providerDisplayName(issue.providerType ?? 'github');
  return {
    kind: 'link',
    label,
    ariaLabel: `${label} ${purpose} #${issue.issueNumber} (opens ${provider} in a new tab)`,
    href,
  };
}

function rowRun(issue: IssueSummary, action: RowActionKind, label: string, pendingLabel: string, ariaLabel: string): RowActionSpec {
  return { kind: 'action', label, pendingLabel, ariaLabel: `${ariaLabel} #${issue.issueNumber}`, action };
}

/** The row's primary control for its needs reason, or null when the reason has none. */
export function rowAction(item: NeedsItem): RowActionSpec | null {
  const issue = item.issue;
  switch (reasonCopy(issue).key) {
    case 'pr_ready':
    case 'merge_skipped': {
      if (!issue.pullRequestUrl) {
        return null;
      }
      const label = `Open ${providerTerminology(issue.providerType ?? 'github').prAbbrev}`;
      return providerLink(issue, label, 'for issue', issue.pullRequestUrl);
    }
    case 'review_skipped':
      return rowRun(issue, 'retry_review', 'Retry review', 'Retrying…', 'Retry review for issue');
    case 'failed':
    case 'no_changes':
      return rowRun(issue, 'retry_issue', 'Retry', 'Retrying…', 'Retry issue');
    case 'triage_failed':
      return rowRun(issue, 'retry_triage', 'Retry triage', 'Retrying…', 'Retry triage for issue');
    case 'publish_failed':
      return rowRun(issue, 'retry_publish', 'Retry publish', 'Retrying publish…', 'Retry publish for issue');
    case 'needs_answers':
      switch (issue.questionPostStatus) {
        case 'posted':
          return providerLink(issue, 'Answer', 'questions for issue', issue.questionCommentUrl || issue.url);
        case 'failed':
          return rowRun(issue, 'retry_post', 'Retry post', 'Retrying post…', 'Retry post for issue');
        default:
          return null;
      }
    case 'publish_blocked':
    case 'branch_diverged':
      return { kind: 'open', label: 'Review', ariaLabel: `Review issue #${issue.issueNumber}` };
    case 'checks_red':
    case 'infra_failed':
      return issue.pullRequestUrl
        ? providerLink(issue, 'Open checks', 'for issue', checksUrl(issue.pullRequestUrl))
        : null;
    case 'review_failed':
    case 'needs_you':
      return null;
  }
}


/** "{n} item(s)" for a band heading's screen-reader count. */
export function countWords(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`;
}
