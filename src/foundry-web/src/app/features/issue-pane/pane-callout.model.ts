import { formatDate } from '@angular/common';
import { IssueDetail, IssueStateDetails, IssueSummary, QUEUED_TIER_STATES } from '../issues/issue.model';
import { SecretFinding, WorkerRunDetail } from '../workers/worker-run.model';
import {
  IssueState,
  MAX_CHECKS_FIX_ATTEMPTS,
  MAX_REBASE_CONFLICT_ATTEMPTS,
  RETRIAGEABLE_STATES,
  RETRYABLE_STATES,
  WORKING_STATES,
} from '../../shared/utils/issue-state';
import { getFailureCategoryDisplay } from '../../shared/utils/failure-category';
import { providerDisplayName, providerTerminology } from '../../shared/utils/provider.util';

export type CalloutTone = 'neutral' | 'working' | 'warning' | 'error' | 'success';

/** An operator action the pane offers beside the callout. */
export type PaneAction =
  | 'stop_worker'
  | 'retry_issue'
  | 'retry_fresh'
  | 'retry_triage'
  | 'skip_triage'
  | 'retry_publish'
  | 'discard_run'
  | 'retriage';

export interface CalloutChip {
  readonly label: string;
  /** Badge modifier class, e.g. `badge--retrying`. */
  readonly cssClass: string;
  readonly ariaLabel: string | null;
}

/** A provider link. The href is raw; the template passes it through `safeHref`. */
export interface CalloutLink {
  readonly label: string;
  readonly href: string;
  readonly ariaLabel: string;
}

export interface CalloutListItem {
  readonly text: string;
  readonly detail: string | null;
  readonly recommendation: string | null;
  /** A short status shown beside the text, e.g. a failing check's conclusion. */
  readonly badge: string | null;
  readonly link: CalloutLink | null;
}

export interface CalloutList {
  readonly heading: string;
  readonly items: readonly CalloutListItem[];
}

/** A fact from a callout that did not win, or a labelled value of the winning one. */
export interface CalloutRow {
  readonly key: string;
  readonly value: string;
  readonly href: string | null;
  readonly ariaLabel: string | null;
  readonly mono: boolean;
}

export interface PaneCallout {
  readonly tone: CalloutTone;
  readonly title: string;
  readonly chip: CalloutChip | null;
  readonly lines: readonly string[];
  readonly list: CalloutList | null;
  readonly secretFindings: readonly SecretFinding[] | null;
  readonly rows: readonly CalloutRow[];
  /** Guidance shown after the list, findings and rows. */
  readonly notes: readonly string[];
  readonly links: readonly CalloutLink[];
  readonly actions: readonly PaneAction[];
}

const INELIGIBLE_LINE =
  'This repository is not dispatchable right now. Check repository settings to make it eligible.';

const CANCEL_NOTE = 'The run finished before the cancel took effect.';

// Callouts of issues waiting for dispatch: they gain the ineligible-repository line.
const DISPATCH_WAITING_STATES: ReadonlySet<IssueState> = new Set<IssueState>([...QUEUED_TIER_STATES, 'detected']);

const FAILURE_STATES: ReadonlySet<IssueState> = new Set<IssueState>(['failed', 'continuable_failed', 'revision_failed']);

// Run states while a worker can still be stopped; an unloaded run counts, as before.
const STOPPABLE_RUN_STATES: ReadonlySet<string> = new Set<string>(['starting', 'running']);

function medium(value: string): string {
  return formatDate(value, 'medium', 'en-US');
}

/** Everything one callout derives from; built once per call. */
class CalloutSource {
  readonly state: IssueState;
  readonly details: IssueStateDetails | null;
  readonly providerType: string;
  readonly providerName: string;
  readonly pullRequest: string;
  readonly prAbbrev: string;
  readonly pullRequestUrl: string | null;
  readonly publishPhase: 'publishing' | 'retrying' | null;

  constructor(
    readonly detail: IssueDetail,
    readonly run: WorkerRunDetail | null,
    readonly summary: IssueSummary | null,
    readonly queuePosition: number | null,
  ) {
    this.state = detail.state;
    this.details = detail.stateDetails;
    this.providerType = summary?.providerType ?? detail.providerType ?? '';
    this.providerName = providerDisplayName(this.providerType) || 'the provider';
    const terms = providerTerminology(this.providerType);
    this.pullRequest = terms.pullRequest.toLowerCase();
    this.prAbbrev = terms.prAbbrev;
    this.pullRequestUrl = summary?.pullRequestUrl ?? detail.pullRequestUrl ?? run?.publish?.pullRequestUrl ?? null;
    this.publishPhase = this._publishPhase();
  }

  get branchText(): string {
    return this.details?.branchName ?? 'the issue branch';
  }

  get checksStillFailing(): boolean {
    return (this.summary?.checksOutcome ?? this.details?.checksOutcome) === 'still_failing';
  }

  prLink(): CalloutLink | null {
    if (this.pullRequestUrl === null) {
      return null;
    }
    return {
      label: `View ${this.prAbbrev}`,
      href: this.pullRequestUrl,
      ariaLabel: `View ${this.pullRequest} for issue #${this.detail.issueNumber}`,
    };
  }

  private _publishPhase(): 'publishing' | 'retrying' | null {
    if (!WORKING_STATES.has(this.state)) {
      return null;
    }
    const phase = this.summary?.publishPhase ?? this.detail.publishPhase ?? null;
    if (phase !== null) {
      return phase;
    }
    switch (this.run?.state) {
      case 'publishing':
        return 'publishing';
      case 'publish_failed':
        return 'retrying';
      default:
        return null;
    }
  }
}

/** A callout under construction: the winning row fills it, then the appends and leftovers run. */
interface Draft {
  tone: CalloutTone;
  title: string;
  chip: CalloutChip | null;
  lines: string[];
  list: CalloutList | null;
  secretFindings: readonly SecretFinding[] | null;
  rows: CalloutRow[];
  notes: string[];
  links: CalloutLink[];
  actions: PaneAction[];
  /** Facts the winning row already shows, so the leftovers do not repeat them. */
  shows: Set<'failure_reason' | 'run_failure' | 'failing_checks' | 'revision'>;
}

function draft(tone: CalloutTone, title: string, lines: string[] = []): Draft {
  return {
    tone,
    title,
    chip: null,
    lines,
    list: null,
    secretFindings: null,
    rows: [],
    notes: [],
    links: [],
    actions: [],
    shows: new Set(),
  };
}

function row(key: string, value: string, extra: Partial<Pick<CalloutRow, 'href' | 'ariaLabel' | 'mono'>> = {}): CalloutRow {
  return { key, value, href: extra.href ?? null, ariaLabel: extra.ariaLabel ?? null, mono: extra.mono ?? false };
}

function item(text: string, extra: Partial<Omit<CalloutListItem, 'text'>> = {}): CalloutListItem {
  return {
    text,
    detail: extra.detail ?? null,
    recommendation: extra.recommendation ?? null,
    badge: extra.badge ?? null,
    link: extra.link ?? null,
  };
}

function failingChecksList(src: CalloutSource): CalloutList | null {
  const checks = src.details?.failingChecks ?? [];
  if (!src.checksStillFailing || checks.length === 0) {
    return null;
  }
  return {
    heading: 'Failing checks',
    items: checks.map((check) =>
      item(check.name, {
        badge: check.conclusion,
        link: check.detailsUrl
          ? { label: 'View details', href: check.detailsUrl, ariaLabel: `View details for failing check ${check.name}` }
          : null,
      })),
  };
}

function runFailureChip(run: WorkerRunDetail | null): CalloutChip | null {
  const category = run?.failureCategory;
  if (!category) {
    return null;
  }
  const display = getFailureCategoryDisplay(category);
  const label = display?.label ?? category.toUpperCase().replace(/_/g, ' ');
  return { label, cssClass: display?.cssClass ?? 'badge--failed', ariaLabel: `Failure category: ${label}` };
}

/** The failure chip, summary, reason and failing checks of a failed run. */
function withFailureBody(d: Draft, src: CalloutSource): Draft {
  d.chip = runFailureChip(src.run);
  if (src.run?.failureSummary) {
    d.lines.push(src.run.failureSummary);
  }
  if (src.details?.failureReason) {
    d.lines.push(src.details.failureReason);
  }
  d.list = failingChecksList(src);
  d.shows.add('run_failure').add('failure_reason').add('failing_checks');
  return d;
}

function revisionTitle(src: CalloutSource): string {
  const s = src.details;
  switch (s?.revisionTriggerKind) {
    case 'failed_checks':
      return `Fixing checks (attempt ${s.checksFixAttempts ?? 1} of ${MAX_CHECKS_FIX_ATTEMPTS})`;
    case 'input_provided':
      return 'Resuming after input';
    case 'rebase_conflict':
      return `Resolving conflicts (attempt ${s.rebaseConflictAttempts ?? 1} of ${MAX_REBASE_CONFLICT_ATTEMPTS})`;
    case 'review_feedback':
      return 'Addressing review';
    default:
      return 'Revision';
  }
}

function revisionMessage(src: CalloutSource): string {
  const running = src.state === 'revision_in_progress';
  switch (src.details?.revisionTriggerKind) {
    case 'failed_checks':
      return running ? 'Fixing checks — a worker is running.' : 'Fix run queued — checks failed on the PR.';
    case 'input_provided':
      return running
        ? 'A worker is applying the answers to the open questions.'
        : 'Queued to apply the answers to the open questions.';
    case 'rebase_conflict':
      return running
        ? 'Resolving conflicts with the default branch — a worker is running.'
        : 'Conflict-resolution run queued — the rebase onto the default branch conflicted.';
    case 'review_feedback':
      return running ? 'A worker is addressing reviewer feedback.' : 'Queued to address reviewer feedback.';
    default:
      return running ? `A worker is revising the ${src.pullRequest}.` : `Queued to revise the ${src.pullRequest}.`;
  }
}

function revisionFailedTitle(src: CalloutSource): string {
  switch (src.details?.revisionTriggerKind) {
    case 'failed_checks':
      return 'Checks fix failed';
    case 'rebase_conflict':
      return 'Conflict resolution failed';
    case 'input_provided':
      return 'Resume failed';
    default:
      return 'Revision failed';
  }
}

/** The issue's place in the dispatch queue, as zero or one line. */
function positionLine(src: CalloutSource): string[] {
  const position = src.queuePosition;
  if (position === null) {
    return [];
  }
  return [position === 1 ? 'Next up.' : `#${position} in the dispatch queue.`];
}

function blockerNumbers(src: CalloutSource): string {
  return (src.details?.blockedBy ?? []).map((b) => `#${b.issueNumber}`).join(', ');
}

function awaitingInputCallout(src: CalloutSource): Draft {
  const awaiting = src.details?.awaitingInput ?? null;
  const fromTriage = awaiting?.resumeTarget === 'retriage';
  const d = fromTriage
    ? draft('warning', 'Triage needs more detail', [
      'The questions are posted on the issue; once a reply settles, Foundry triages again.',
    ])
    : draft('warning', 'Needs answers', [
      'Answer the questions on the issue. Once a reply settles, Foundry resumes the work.',
    ]);
  if (awaiting === null) {
    return d;
  }
  d.list = {
    heading: fromTriage ? 'Questions from triage' : 'Open questions',
    items: awaiting.questions.map((q) =>
      item(q.question, { detail: q.context || null, recommendation: q.recommendation ?? null })),
  };
  switch (awaiting.postStatus) {
    case 'posted':
      d.notes.push(
        awaiting.postedAt
          ? `Posted ${medium(awaiting.postedAt)} — waiting for a reply.`
          : 'Posted — waiting for a reply.',
      );
      if (awaiting.commentUrl) {
        d.links.push({
          label: 'View question',
          href: awaiting.commentUrl,
          ariaLabel: `View the questions on issue #${src.detail.issueNumber}`,
        });
      }
      break;
    case 'failed':
      d.notes.push(
        `Posting the questions failed: ${awaiting.postFailureReason ?? 'unknown reason'}.`
          + (awaiting.nextPostAttemptAt ? ` Next attempt at ${medium(awaiting.nextPostAttemptAt)}.` : ''),
      );
      break;
    default:
      d.notes.push('Posting the questions…');
  }
  return d;
}

function publishBlockedCallout(src: CalloutSource): Draft {
  const findings = src.run?.secretFindings ?? [];
  const count = findings.length;
  const secrets = count === 0 ? 'possible secrets' : count === 1 ? '1 possible secret' : `${count} possible secrets`;
  const d = draft('error', 'Push blocked', [
    `The diff gate found ${secrets} in the run's changes, so nothing was pushed to ${src.branchText}.`,
  ]);
  d.secretFindings = count > 0 ? findings : null;
  d.notes.push(
    'Foundry never publishes a run with a detected secret. Discard the run, then retry the issue to start a new '
      + 'worker from the branch. If a finding is a real credential, rotate it: the worker had it in its workspace.'
      + (src.run?.discardsAt
        ? ` If no one acts, the run is discarded automatically on ${medium(src.run.discardsAt)}.`
        : ''),
  );
  return d;
}

function publishFailedCallout(src: CalloutSource): Draft {
  const retry = src.run?.publishRetry ?? null;
  const d = draft('error', 'Publish failed', [
    retry ? `Publishing failed after ${retry.maxAttempts} attempts: ${retry.reason}.` : 'Publishing failed.',
    'Fix the cause, then use Retry publish.',
  ]);
  if (src.run?.discardsAt) {
    d.lines.push(`Unpublished work is discarded on ${medium(src.run.discardsAt)}.`);
  }
  return d;
}

function branchDivergedCallout(src: CalloutSource): Draft {
  const s = src.details;
  const forcePushProtected = s?.divergenceReason === 'force-push-protected';
  const d = draft('warning', 'Branch diverged', [
    forcePushProtected
      ? `The worker rebased the issue branch onto the default branch, but ${src.providerName} protects the issue `
        + "branch from force pushes. Foundry pushed the run's work to a separate branch instead."
      : "Someone pushed to the issue branch while the worker ran, and the run's work could not join it. Foundry "
        + "pushed the run's work to a separate branch instead.",
  ]);
  const branch = s?.branchName ?? '';
  const sideBranch = s?.sideBranchName ?? '';
  d.rows.push(
    row('Issue branch', branch, {
      href: s?.branchUrl ?? null,
      ariaLabel: `View branch ${branch} on ${src.providerName}`,
      mono: true,
    }),
    row("Run's work", sideBranch, {
      href: s?.sideBranchUrl ?? null,
      ariaLabel: `View the run's branch ${sideBranch} on ${src.providerName}`,
      mono: true,
    }),
  );
  d.notes.push(
    forcePushProtected
      ? `Replace the issue branch with the run's branch on ${src.providerName}, allowing a force push for it. `
        + 'Foundry continues automatically once the issue branch holds the run\'s work, or use Retry issue.'
      : `Merge the run's branch into the issue branch on ${src.providerName}. Foundry continues automatically once `
        + 'the merge lands, or use Retry issue.',
  );
  return d;
}

function publishRetryingCallout(src: CalloutSource): Draft {
  const retry = src.run?.publishRetry ?? null;
  if (retry === null) {
    const d = draft('warning', 'Retrying publish', ['Publishing failed. Next attempt shortly.']);
    d.chip = { label: 'Publish retry', cssClass: 'badge--retrying', ariaLabel: null };
    return d;
  }
  const d = draft('warning', 'Retrying publish', [
    `Publishing failed: ${retry.reason}. `
      + (retry.nextAttemptAt ? `Next attempt at ${medium(retry.nextAttemptAt)}.` : 'Next attempt shortly.'),
  ]);
  d.chip = {
    label: `Publish attempt ${retry.attemptNumber} of ${retry.maxAttempts}`,
    cssClass: 'badge--retrying',
    ariaLabel: null,
  };
  return d;
}

function reviewCallout(src: CalloutSource): Draft {
  const link = src.prLink();
  let d: Draft;
  if (src.checksStillFailing) {
    const list = failingChecksList(src);
    if (list === null) {
      d = draft('error', 'Checks failing', [
        `Checks failed, but no failing check could be identified. Open the ${src.pullRequest}'s checks on `
          + `${src.providerName}.`,
      ]);
    } else {
      d = draft('error', 'Checks still failing', [
        `Foundry stopped fixing checks after ${MAX_CHECKS_FIX_ATTEMPTS} attempts. Review the ${src.pullRequest} on `
          + `${src.providerName}.`,
      ]);
      d.list = list;
    }
    d.shows.add('failing_checks');
  } else {
    d = draft('success', `${src.prAbbrev} ready for review`, [
      `Review the ${src.pullRequest} on ${src.providerName}. Foundry addresses review comments in a revision run.`,
    ]);
  }
  if (link !== null) {
    d.links.push(link);
  }
  return d;
}

/** The first row of the Callout model table (#862) that matches the issue. */
function winningCallout(src: CalloutSource): Draft {
  const state = src.state;
  const s = src.details;
  switch (state) {
    case 'completed': {
      const d = draft('success', 'Completed', [s?.completedAt ? `Completed ${medium(s.completedAt)}.` : 'Completed.']);
      const link = src.prLink();
      if (link !== null) {
        d.links.push(link);
      }
      return d;
    }
    case 'publish_blocked':
      return publishBlockedCallout(src);
    case 'publish_failed':
      return publishFailedCallout(src);
    case 'branch_diverged':
      return branchDivergedCallout(src);
  }
  if (src.publishPhase === 'publishing') {
    return draft('working', 'Publishing', [`Foundry is publishing the run's commits to ${src.branchText}.`]);
  }
  if (src.publishPhase === 'retrying') {
    return publishRetryingCallout(src);
  }
  switch (state) {
    case 'awaiting_input':
      return awaitingInputCallout(src);
    case 'triage_failed':
      return withFailureBody(
        draft('error', 'Triage did not finish', [
          'Retry triage to run it again, or skip triage to queue the issue without a readiness check.',
        ]),
        src,
      );
    case 'failed':
      return withFailureBody(draft('error', src.checksStillFailing ? 'Checks still failing' : 'Run failed'), src);
    case 'continuable_failed': {
      const d = withFailureBody(draft('error', 'Run stopped with work kept'), src);
      d.lines.push(
        "The branch keeps the run's commits. Retry continues from them; Retry fresh starts over from the default branch.",
      );
      return d;
    }
    case 'revision_failed':
      return withFailureBody(draft('error', revisionFailedTitle(src)), src);
    case 'unchanged': {
      const d = draft('neutral', 'No changes', ['The run finished without changing any files.']);
      if (s?.failureReason) {
        d.lines.push(s.failureReason);
      }
      d.shows.add('failure_reason');
      return d;
    }
    case 'revision_in_progress':
    case 'revision_queued': {
      const d = draft(
        state === 'revision_in_progress' ? 'working' : 'neutral',
        revisionTitle(src),
        [revisionMessage(src), ...positionLine(src)],
      );
      d.shows.add('revision');
      return d;
    }
    case 'in_progress':
      return draft('working', 'Implementing', ['A worker is implementing this issue.']);
    case 'triage_in_progress':
      return draft('working', 'Triaging', ['A triage worker is reading the issue and the repository.']);
    case 'rebase_pending':
      return draft('working', 'Rebasing', ['Foundry is rebasing the branch onto the default branch.']);
    case 'checks_pending':
      return draft('working', 'Checks running', [`Waiting for CI checks on the ${src.pullRequest}.`]);
    case 'review':
      return reviewCallout(src);
    case 'detected':
    case 'triage_queued':
      return draft('neutral', 'Waiting for triage', ['Waiting for a worker to triage this issue.', ...positionLine(src)]);
    case 'queued':
      return draft('neutral', 'Queued', positionLine(src));
    case 'continuation_queued':
      return draft('neutral', 'Queued', [...positionLine(src), 'Continues from the issue branch.']);
    case 'blocked': {
      const numbers = blockerNumbers(src);
      return draft('neutral', 'Blocked', [
        numbers ? `Waiting on ${numbers}. It queues once they close.` : 'Waiting on its blockers. It queues once they close.',
      ]);
    }
    case 'triage_blocked': {
      const d = draft('neutral', 'Waiting for blockers', ['Waiting for its blockers to close. Triage runs once they do.']);
      const blockers = s?.blockedBy ?? [];
      if (blockers.length > 0) {
        d.list = {
          heading: 'Blocked by',
          items: blockers.map((b) => item(`#${b.issueNumber}`, { detail: b.reason ? `Triage: ${b.reason}` : null })),
        };
      }
      return d;
    }
  }
  // An issue state this dashboard does not know yet renders as itself.
  return draft('neutral', state);
}

function actionsFor(src: CalloutSource): PaneAction[] {
  const state = src.state;
  const actions: PaneAction[] = [];
  // Stop worker is hidden while the run publishes, so a push is never cut off mid-way.
  if (
    WORKING_STATES.has(state)
    && src.publishPhase === null
    && (src.run === null || STOPPABLE_RUN_STATES.has(src.run.state))
  ) {
    actions.push('stop_worker');
  }
  if (state === 'publish_failed') {
    actions.push('retry_publish');
  }
  if (state === 'publish_failed' || state === 'publish_blocked') {
    actions.push('discard_run');
  }
  if (RETRYABLE_STATES.has(state)) {
    actions.push('retry_issue');
  }
  if (state === 'continuable_failed') {
    actions.push('retry_fresh');
  }
  if (state === 'triage_failed') {
    actions.push('retry_triage', 'skip_triage');
  }
  if (RETRIAGEABLE_STATES.has(state)) {
    actions.push('retriage');
  }
  return actions;
}

function transientRetryLine(src: CalloutSource): string | null {
  const retry = src.details?.transientRetry ?? null;
  if (retry === null) {
    return null;
  }
  if (retry.isExhausted) {
    let hint = '';
    if (src.state === 'triage_failed') {
      hint = ' Use Retry triage to run it again.';
    } else if (FAILURE_STATES.has(src.state)) {
      hint = ' Use Retry issue to try again manually.';
    }
    return `Automatic retries exhausted after ${retry.maxAttempts} attempts.${hint}`;
  }
  const next = retry.nextAttemptDueAt
    ? `Automatic retry pending — next attempt at ${medium(retry.nextAttemptDueAt)}.`
    : 'Automatic retry pending — next attempt shortly.';
  return `Attempt ${retry.attemptNumber} of ${retry.maxAttempts}. ${next}`;
}

/** The facts of callouts that did not win, kept as rows so the priority hides nothing. */
function addLeftovers(d: Draft, src: CalloutSource): void {
  const s = src.details;
  if (!d.shows.has('revision') && s?.revisionTriggerKind && (src.state === 'revision_in_progress' || src.state === 'revision_queued')) {
    d.rows.push(row('Revision', revisionTitle(src)));
  }
  const chip = runFailureChip(src.run);
  if (!d.shows.has('run_failure') && chip !== null) {
    d.rows.push(row('Run failure', src.run?.failureSummary ? `${chip.label} — ${src.run.failureSummary}` : chip.label));
  }
  if (!d.shows.has('failure_reason') && s?.failureReason) {
    d.rows.push(row('Failure reason', s.failureReason));
  }
  const checks = d.shows.has('failing_checks') ? null : failingChecksList(src);
  if (checks !== null) {
    if (d.list === null) {
      d.list = checks;
    } else {
      d.rows.push(row('Failing checks', checks.items.map((c) => `${c.text} (${c.badge})`).join(', ')));
    }
  }
}

/**
 * The one callout the issue pane leads with, chosen by the fixed priority of the Callout model
 * table (#862): the first matching row wins, the facts of the rows that did not win become rows,
 * and the cancel note, ineligible-repository and transient-retry lines are appended.
 *
 * `summary` carries what the detail does not (provider, pull request, publish phase); null on a
 * deep link the page holds no summary for. `queuePosition` is the issue's 1-based dispatch position.
 */
export function buildCallout(
  detail: IssueDetail,
  run: WorkerRunDetail | null,
  summary: IssueSummary | null,
  queuePosition: number | null,
): PaneCallout {
  const src = new CalloutSource(detail, run, summary, queuePosition);
  const d = winningCallout(src);
  d.actions = actionsFor(src);

  addLeftovers(d, src);

  if (run !== null && run.state === 'completed' && run.cancelRequestedAt !== null) {
    d.lines.push(CANCEL_NOTE);
  }
  const eligibility = summary?.repositoryEligibilityStatus ?? detail.repositoryEligibilityStatus;
  if (DISPATCH_WAITING_STATES.has(src.state) && (eligibility === 'ineligible' || eligibility === 'unreachable')) {
    d.lines.push(INELIGIBLE_LINE);
  }
  const retryLine = transientRetryLine(src);
  if (retryLine !== null) {
    d.lines.push(retryLine);
  }

  return {
    tone: d.tone,
    title: d.title,
    chip: d.chip,
    lines: d.lines,
    list: d.list,
    secretFindings: d.secretFindings,
    rows: d.rows,
    notes: d.notes,
    links: d.links,
    actions: d.actions,
  };
}
