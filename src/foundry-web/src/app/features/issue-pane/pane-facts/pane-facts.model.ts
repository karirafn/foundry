import {
  IssueDetail,
  IssueKind,
  IssueKindClassification,
  IssueSummary,
  RunStats,
  TriageVerdictDetails,
} from '../../issues/issue.model';
import { PUBLISH_RUN_STATES, RevertedFile, WorkerRunDetail } from '../../workers/worker-run.model';
import { MAX_CHECKS_FIX_ATTEMPTS, MAX_REBASE_CONFLICT_ATTEMPTS } from '../../../shared/utils/issue-state';
import { rebaseOutcomeLabel } from '../../issues/rebase-outcome.format';
import { formatCost, formatDuration, formatTokenTotal } from '../../issues/run-stats.format';

const SHORT_SHA_LENGTH = 7;
const SHORT_RUN_ID_LENGTH = 8;

// A result longer than this clamps to 4 lines behind Show more.
const RESULT_CLAMP_LINES = 4;
const RESULT_CLAMP_CHARS = 320;

const KIND_LABELS: Readonly<Record<IssueKind, string>> = {
  feature: 'Feature',
  bug: 'Bug',
  refactor: 'Refactor',
  documentation: 'Documentation',
};

const KIND_SOURCE_NOTES: Readonly<Record<IssueKindClassification['source'], string>> = {
  labelled: 'From label',
  triaged: 'From triage',
  unclassified: 'No kind label, and triage has not classified it',
};

const TRIAGE_OUTCOMES = {
  ready: { label: 'Ready', tone: 'success' },
  needs_input: { label: 'Needs input', tone: 'warning' },
  blocked: { label: 'Blocked', tone: 'neutral' },
} as const satisfies Record<TriageVerdictDetails['outcome'], { label: string; tone: TriageTone }>;

// States in which the triage verdict is stale or not yet decided: the callout tells the triage
// story instead, so the Triage row is hidden.
const TRIAGE_CALLOUT_STATES: ReadonlySet<string> = new Set<string>([
  'detected',
  'triage_queued',
  'triage_blocked',
  'triage_in_progress',
  'triage_failed',
]);

export type TriageTone = 'success' | 'warning' | 'neutral';

export interface TriageVerdictFact {
  readonly outcomeLabel: string;
  readonly tone: TriageTone;
  readonly kindLabel: string;
  /** ISO-8601. */
  readonly decidedAt: string;
  readonly note: string | null;
}

export interface BlockerFact {
  readonly number: string;
  readonly reason: string | null;
}

/** One row of the pane's Facts list. Each type renders its own way; empty rows are never built. */
export type PaneFact =
  | { readonly type: 'text'; readonly key: string; readonly value: string; readonly mono: boolean }
  | {
      readonly type: 'kind';
      readonly key: 'Kind';
      readonly label: string;
      readonly known: boolean;
      readonly sourceNote: string;
      /** The sentence beside the Triage differs chip; null when triage agrees with the label. */
      readonly triageDiffers: string | null;
    }
  /** `verdict` is null when the issue was queued without a readiness check. */
  | { readonly type: 'triage'; readonly key: 'Triage'; readonly verdict: TriageVerdictFact | null }
  | {
      readonly type: 'published';
      readonly key: 'Published';
      readonly shortSha: string;
      readonly fullSha: string;
      /** Raw; the template passes it through `safeHref`. */
      readonly href: string | null;
      readonly commits: string;
    }
  | { readonly type: 'reverted_files'; readonly key: 'Reverted files'; readonly files: readonly RevertedFile[] }
  | { readonly type: 'run'; readonly key: 'Run'; readonly role: string | null; readonly shortId: string; readonly id: string }
  | { readonly type: 'blockers'; readonly key: 'Blocked by'; readonly blockers: readonly BlockerFact[] }
  | { readonly type: 'labels'; readonly key: 'Labels'; readonly labels: readonly string[] }
  | { readonly type: 'date'; readonly key: string; readonly iso: string }
  | { readonly type: 'result'; readonly key: 'Run result'; readonly text: string; readonly long: boolean }
  | { readonly type: 'log'; readonly key: 'Log'; readonly workerRunId: string };

function kindLabel(kind: string | null): string {
  if (kind === null) {
    return 'Unclassified';
  }
  return KIND_LABELS[kind as IssueKind] ?? kind;
}

/** The kind as it reads mid-sentence, e.g. "a refactor" or "documentation". */
function kindPhrase(kind: string): string {
  const lower = kindLabel(kind).toLowerCase();
  return kind === 'documentation' ? lower : `a ${lower}`;
}

function kindFact(kind: IssueKindClassification): PaneFact {
  const known = kind.source !== 'unclassified';
  return {
    type: 'kind',
    key: 'Kind',
    label: known ? kindLabel(kind.kind) : 'Unclassified',
    known,
    sourceNote: KIND_SOURCE_NOTES[kind.source] ?? '',
    triageDiffers: kind.conflictingTriageKind
      ? `Triage read it as ${kindPhrase(kind.conflictingTriageKind)}. The label decides; nothing changes.`
      : null,
  };
}

function triageVerdict(t: TriageVerdictDetails): TriageVerdictFact {
  // The outcome arrives as a server string, so an outcome added later renders as itself.
  const known = Object.prototype.hasOwnProperty.call(TRIAGE_OUTCOMES, t.outcome) ? TRIAGE_OUTCOMES[t.outcome] : null;
  let note: string | null = null;
  if (t.outcome === 'blocked') {
    note = 'Triage found open issues this depends on.';
  } else if (t.outcome === 'needs_input') {
    note = t.questions.length === 1 ? '1 question asked' : `${t.questions.length} questions asked`;
  }
  return {
    outcomeLabel: known?.label ?? t.outcome,
    tone: known?.tone ?? 'neutral',
    kindLabel: kindLabel(t.kind),
    decidedAt: t.decidedAt,
    note,
  };
}

/**
 * The Triage row: the latest verdict, or that the issue was queued without one. Hidden while the
 * callout tells the triage story, because a verdict then predates the run.
 */
function triageFact(d: IssueDetail): PaneFact | null {
  if (TRIAGE_CALLOUT_STATES.has(d.state)) {
    return null;
  }
  if (d.state === 'awaiting_input' && d.stateDetails?.awaitingInput?.resumeTarget === 'retriage') {
    return null;
  }
  if (d.triage) {
    return { type: 'triage', key: 'Triage', verdict: triageVerdict(d.triage) };
  }
  if (d.state === 'queued' || d.state === 'blocked') {
    return { type: 'triage', key: 'Triage', verdict: null };
  }
  return null;
}

function commitsText(count: number): string {
  if (count === 0) {
    return 'No commits';
  }
  return count === 1 ? '1 commit' : `${count} commits`;
}

function runsText(stats: RunStats): string {
  const parts = [stats.runCount === 1 ? '1 run' : `${stats.runCount} runs`];
  if (stats.durationMs !== null) {
    parts.push(formatDuration(stats.durationMs));
  }
  if (stats.numTurns !== null) {
    parts.push(stats.numTurns === 1 ? '1 turn' : `${stats.numTurns} turns`);
  }
  if (stats.totalCostUsd !== null) {
    parts.push(formatCost(stats.totalCostUsd));
  }
  parts.push(`${formatTokenTotal(stats.inputTokens)} in / ${formatTokenTotal(stats.outputTokens)} out`);
  return parts.join(' · ');
}

function text(key: string, value: string | null | undefined, mono = false): PaneFact | null {
  return value ? { type: 'text', key, value, mono } : null;
}

function date(key: string, iso: string | null | undefined): PaneFact | null {
  return iso ? { type: 'date', key, iso } : null;
}

function attempts(key: string, count: number | null | undefined, cap: number): PaneFact | null {
  return count ? text(key, `${count} of ${cap}`) : null;
}

function isLong(result: string): boolean {
  return result.length > RESULT_CLAMP_CHARS || result.split('\n').length > RESULT_CLAMP_LINES;
}

/**
 * The pane's Facts rows (#863), in the order of the Facts table; a row with no value is left out.
 * `summary` carries the run stats, which the detail does not; null on a deep link the page holds no
 * summary for.
 */
export function buildFacts(
  detail: IssueDetail,
  run: WorkerRunDetail | null,
  summary: IssueSummary | null,
): readonly PaneFact[] {
  const s = detail.stateDetails;
  const stats = summary?.runStats ?? null;
  const runId = run?.workerRunId ?? s?.workerRunId ?? null;
  const publish = run?.publish ?? null;
  const blockers = s?.blockedBy ?? [];

  const rows: (PaneFact | null)[] = [
    detail.kind ? kindFact(detail.kind) : null,
    triageFact(detail),
    text('Branch', s?.branchName, true),
    publish === null
      ? null
      : {
          type: 'published',
          key: 'Published',
          shortSha: publish.headSha.slice(0, SHORT_SHA_LENGTH),
          fullSha: publish.headSha,
          href: publish.headCommitUrl,
          commits: commitsText(publish.branchCommitCount),
        },
    run !== null && run.revertedFiles.length > 0
      ? { type: 'reverted_files', key: 'Reverted files', files: run.revertedFiles }
      : null,
    stats !== null && stats.runCount > 0 ? text('Runs', runsText(stats)) : null,
    runId === null
      ? null
      : { type: 'run', key: 'Run', role: run?.role ?? null, shortId: runId.slice(0, SHORT_RUN_ID_LENGTH), id: runId },
    attempts('Checks fixes', s?.checksFixAttempts, MAX_CHECKS_FIX_ATTEMPTS),
    attempts('Conflict fixes', s?.rebaseConflictAttempts, MAX_REBASE_CONFLICT_ATTEMPTS),
    text('Rebase', rebaseOutcomeLabel(s?.rebaseOutcome, s?.rebaseSkippedReason)),
    blockers.length > 0
      ? {
          type: 'blockers',
          key: 'Blocked by',
          blockers: blockers.map((b) => ({ number: `#${b.issueNumber}`, reason: b.reason ? `Triage: ${b.reason}` : null })),
        }
      : null,
    detail.labels.length > 0 ? { type: 'labels', key: 'Labels', labels: detail.labels } : null,
    text('Author', detail.author),
    date('Feedback cutoff', s?.feedbackCutoffAt),
    date('Failed at', s?.failedAt),
    date('Completed', s?.completedAt),
    run?.resultText ? { type: 'result', key: 'Run result', text: run.resultText, long: isLong(run.resultText) } : null,
    // Publish states keep no log view: the agent has stopped and Foundry owns the commits.
    run !== null && run.hasStoredLog && !PUBLISH_RUN_STATES.has(run.state)
      ? { type: 'log', key: 'Log', workerRunId: run.workerRunId }
      : null,
  ];
  return rows.filter((row): row is PaneFact => row !== null);
}
