import { CodeStep, IssueStage, IssueSummary } from '../issues/issue.model';
import { WORKING_STATES } from '../../shared/utils/issue-state';
import { providerTerminology } from '../../shared/utils/provider.util';

/** The stages the board shows; a completed (`done`) issue leaves the board. */
export type BoardStage = Exclude<IssueStage, 'done'>;

export interface StageDef {
  readonly id: BoardStage;
  readonly label: string;
  /** Whether issues wait for dispatch in this stage, so its column has a queue section. */
  readonly hasQueue: boolean;
}

/** The columns, in flow order. The grid derives its column count from this list. */
export const STAGES: readonly StageDef[] = [
  { id: 'triage', label: 'Triage', hasQueue: true },
  { id: 'implement', label: 'Implement', hasQueue: true },
  { id: 'awaiting_review', label: 'Your review', hasQueue: false },
];

/** The label that starts work on an issue; the backend constant `WebhookRelevance.TriggerLabel`. */
export const TRIGGER_LABEL = 'foundry';

export type BoardCell = 'queue' | 'flow' | 'needs';

export type ReasonTone = 'error' | 'warning' | 'ready' | 'neutral';

export interface NeedsReason {
  readonly label: string;
  readonly tone: ReasonTone;
}

export type StepTone = 'working' | 'warning';

export const CODE_STEP_LABELS: Readonly<Record<CodeStep, string>> = {
  publishing: 'Publishing…',
  publish_retrying: 'Retrying publish…',
  rebasing: 'Rebasing…',
  checks: 'Checks running…',
};

export function codeStepTone(step: CodeStep): StepTone {
  return step === 'publish_retrying' ? 'warning' : 'working';
}

/** One card as the board renders it. */
export interface BoardCard {
  readonly issue: IssueSummary;
  readonly stage: BoardStage;
  readonly cell: BoardCell;
  /** Global dispatch position, shared across both queues; null for cards that are not dispatchable. */
  readonly position: number | null;
  readonly ineligible: boolean;
  /** The role the next run takes, for a card awaiting dispatch. */
  readonly nextRole: BoardStage | null;
  /** Why a queue card has no position yet (`Detected`, `Blocked`), or null. */
  readonly queueMarker: string | null;
  readonly reason: NeedsReason | null;
  readonly running: boolean;
}

export interface BoardColumn {
  readonly stage: StageDef;
  readonly queue: readonly BoardCard[];
  readonly flow: readonly BoardCard[];
  readonly needs: readonly BoardCard[];
  readonly count: number;
}

const QUEUE_MARKERS: Readonly<Partial<Record<IssueSummary['state'], string>>> = {
  detected: 'Detected',
  blocked: 'Blocked',
  triage_blocked: 'Blocked',
};

const FAILED: NeedsReason = { label: 'Failed', tone: 'error' };

const STATE_REASONS: Readonly<Partial<Record<IssueSummary['state'], NeedsReason>>> = {
  awaiting_input: { label: 'Needs answers', tone: 'warning' },
  unchanged: { label: 'No changes', tone: 'neutral' },
  failed: FAILED,
  continuable_failed: FAILED,
  revision_failed: FAILED,
  triage_failed: FAILED,
  publish_failed: { label: 'Publish failed', tone: 'error' },
  publish_blocked: { label: 'Publish blocked', tone: 'error' },
  branch_diverged: { label: 'Branch diverged', tone: 'warning' },
};

// A state the server marks as needing action that this table doesn't name yet still reads as such.
const NEEDS_YOU: NeedsReason = { label: 'Needs you', tone: 'neutral' };

export function isBoardStage(stage: IssueStage | null): stage is BoardStage {
  return stage !== null && stage !== 'done';
}

/** The cell an issue sits in, from the server's placement only. */
export function cellFor(issue: IssueSummary): BoardCell {
  if (issue.needsAction) {
    return 'needs';
  }
  return issue.awaitsDispatch ? 'queue' : 'flow';
}

/** Why the issue needs a person, or null when it doesn't. */
export function needsReason(issue: IssueSummary): NeedsReason | null {
  if (!issue.needsAction) {
    return null;
  }
  if (issue.state === 'review') {
    if (issue.checksOutcome === 'still_failing') {
      return { label: 'Checks red', tone: 'error' };
    }
    return {
      label: `${providerTerminology(issue.providerType ?? 'github').prAbbrev} ready`,
      tone: 'ready',
    };
  }
  return STATE_REASONS[issue.state] ?? NEEDS_YOU;
}

/** A worker is running for the issue (not Foundry publishing its commits). */
export function isRunning(issue: IssueSummary): boolean {
  return WORKING_STATES.has(issue.state) && issue.publishPhase == null;
}

function isIneligible(issue: IssueSummary): boolean {
  return (
    issue.repositoryEligibilityStatus === 'ineligible' ||
    issue.repositoryEligibilityStatus === 'unreachable'
  );
}

// Queue order (ADR 0067): dispatchable issues by position, then eligible ones that can't be queued
// yet, then ineligible ones; each group keeps the server's array order (sort is stable).
function queueRank(card: BoardCard): number {
  if (card.position !== null) {
    return 0;
  }
  return card.ineligible ? 2 : 1;
}

/**
 * Lays the issues out in columns. `dispatchQueueIds` is the dispatchable queue in dispatch order;
 * a card's position is its index there, so the numbering is shared across the queues.
 */
export function buildBoardColumns(
  issues: readonly IssueSummary[],
  dispatchQueueIds: readonly string[],
): BoardColumn[] {
  const positions = new Map<string, number>(dispatchQueueIds.map((id, i) => [id, i + 1]));
  const cells = new Map<BoardStage, Record<BoardCell, BoardCard[]>>(
    STAGES.map((s) => [s.id, { queue: [], flow: [], needs: [] }]),
  );

  for (const issue of issues) {
    const stage = issue.stage;
    if (!isBoardStage(stage)) {
      continue;
    }
    const cell = cellFor(issue);
    const ineligible = cell === 'queue' && isIneligible(issue);
    cells.get(stage)?.[cell].push({
      issue,
      stage,
      cell,
      position: ineligible ? null : (positions.get(issue.id) ?? null),
      ineligible,
      nextRole: issue.awaitsDispatch ? stage : null,
      queueMarker: cell === 'queue' ? (QUEUE_MARKERS[issue.state] ?? null) : null,
      reason: needsReason(issue),
      running: isRunning(issue),
    });
  }

  return STAGES.map((stage) => {
    const { queue, flow, needs } = cells.get(stage.id)!;
    const ordered = [...queue].sort((a, b) => queueRank(a) - queueRank(b));
    return { stage, queue: ordered, flow, needs, count: queue.length + flow.length + needs.length };
  });
}
