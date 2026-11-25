import {
  AwaitingInputDetails,
  Clarification,
  ClarificationDecision,
  DecisionGround,
  InputPart,
  InputQuestion,
  IssueDetail,
} from '../../issues/issue.model';
import { providerDisplayName } from '../../../shared/utils/provider.util';
import { formatWaiting } from '../../../shared/utils/format-waiting';

const SHORT_SHA_LENGTH = 7;
/** A comment longer than this many lines or characters starts collapsed with an expand control. */
const LONG_COMMENT_LINES = 6;
const LONG_COMMENT_CHARACTERS = 480;

export type RoundStatus = 'open' | 'answered' | 'unanswered' | 'decided';

export type DecisionBlock =
  | { readonly type: 'lines'; readonly text: string }
  | { readonly type: 'table'; readonly header: readonly string[]; readonly rows: readonly (readonly string[])[] };

export interface DecisionView {
  readonly question: string;
  readonly ground: DecisionGround;
  /** Null for a ground this dashboard does not know. */
  readonly groundLabel: string | null;
  readonly blocks: readonly DecisionBlock[];
  readonly evidence: readonly string[];
}

export type InputPartView =
  | { readonly type: 'comments'; readonly text: string; readonly long: boolean }
  | { readonly type: 'note'; readonly text: string }
  | {
    readonly type: 'branch';
    /** `Branch updated` for a move, `Branch recreated` when the branch did not exist at post time. */
    readonly label: string;
    /** `abc1234..def5678` for a move, the new head alone for a recreation. */
    readonly shortRange: string;
    readonly fullRange: string;
    readonly href: string | null;
    readonly ariaLabel: string;
  };

export type PostView =
  | { readonly status: 'pending' }
  | { readonly status: 'posted'; readonly postedAt: string | null; readonly commentUrl: string | null }
  | { readonly status: 'failed'; readonly reason: string; readonly nextAttemptAt: string | null };

export type ReminderView =
  | { readonly status: 'sent'; readonly at: string }
  | { readonly status: 'due'; readonly at: string }
  /** The reminder is scheduled once the questions are posted. */
  | { readonly status: 'after_post' };

/** What only the open round shows: how its post stands, how long it has waited, and its reminder. */
export interface OpenRoundView {
  readonly post: PostView;
  /** Coarse time since the round opened, e.g. `2d`. */
  readonly waited: string;
  /** Null when no reminder will be posted: its post failed permanently. */
  readonly reminder: ReminderView | null;
}

export type QuestionView =
  | {
    readonly kind: 'legacy';
    readonly question: string;
    readonly context: string | null;
    readonly recommendation: string | null;
  }
  | {
    readonly kind: 'structured';
    readonly question: string;
    readonly problem: string;
    readonly impact: string;
    /** In letter order, exactly one of them recommended. */
    readonly options: readonly OptionView[];
    readonly recommended: OptionView;
    readonly reason: string;
    readonly evidence: readonly string[];
  };

export interface OptionView {
  /** By position: `a` for the first option. */
  readonly letter: string;
  readonly text: string;
  readonly recommended: boolean;
}

export interface ClarificationRoundView {
  /** 1-based, oldest round first. */
  readonly number: number;
  readonly status: RoundStatus;
  readonly statusLabel: string;
  readonly questions: readonly QuestionView[];
  readonly askedAt: string;
  readonly answeredAt: string | null;
  readonly input: readonly InputPartView[];
  /** The details triage settled itself in the round; empty when it settled none. */
  readonly decisions: readonly DecisionView[];
  /** Set only for the open round of an awaiting-input issue. */
  readonly open: OpenRoundView | null;
}

const STATUS_LABELS: Record<RoundStatus, string> = {
  open: 'Open',
  answered: 'Answered',
  unanswered: 'Continued without answer',
  decided: 'Triage decided',
};

const GROUND_LABELS: Record<DecisionGround, string> = {
  settled_business: 'Settled by existing behaviour',
  implementation_detail: 'Implementation detail',
};

/** The issue's clarification log as the pane's Clarifications section renders it, oldest round first. */
export function buildClarifications(
  detail: IssueDetail,
  providerType: string,
  now: number,
): ClarificationRoundView[] {
  const providerName = providerDisplayName(providerType);
  const awaiting = detail.state === 'awaiting_input' ? detail.stateDetails?.awaitingInput ?? null : null;
  return detail.clarifications.map((round, index) => {
    const status = roundStatus(round);
    return {
      number: index + 1,
      status,
      statusLabel: STATUS_LABELS[status],
      questions: round.questions.map(questionView),
      askedAt: round.askedAt,
      answeredAt: round.answeredAt,
      input: (round.input ?? []).flatMap((part) => partView(part, providerName)),
      decisions: (round.decisions ?? []).map(decisionView),
      open: status === 'open' && awaiting !== null ? openRound(round, awaiting, now) : null,
    };
  });
}

function roundStatus(round: Clarification): RoundStatus {
  // A decided round has no input either, so it is told apart before the open test.
  if (round.questions.length === 0 && (round.decisions ?? []).length > 0) {
    return 'decided';
  }
  if (round.input === null) {
    return 'open';
  }
  return round.input.some((part) => part.kind === 'continuedWithoutAnswer') ? 'unanswered' : 'answered';
}

// An unknown kind from a newer server renders nothing rather than breaking the section.
function partView(part: InputPart, providerName: string): InputPartView[] {
  switch (part.kind) {
    case 'comments':
      return [{ type: 'comments', text: part.text, long: isLong(part.text) }];
    case 'descriptionEdited':
      return [{ type: 'note', text: 'Issue description edited' }];
    case 'continuedWithoutAnswer':
      return [{ type: 'note', text: 'Continued without answer' }];
    case 'branchUpdated':
      return [branchView(part, providerName)];
    default:
      return [];
  }
}

function branchView(
  part: Extract<InputPart, { kind: 'branchUpdated' }>,
  providerName: string,
): InputPartView {
  const toShort = part.toSha.slice(0, SHORT_SHA_LENGTH);
  const fromShort = part.fromSha?.slice(0, SHORT_SHA_LENGTH) ?? null;
  const where = providerName ? ` on ${providerName}` : '';
  return {
    type: 'branch',
    label: fromShort === null ? 'Branch recreated' : 'Branch updated',
    shortRange: fromShort === null ? toShort : `${fromShort}..${toShort}`,
    fullRange: part.fromSha === null ? part.toSha : `${part.fromSha}..${part.toSha}`,
    href: part.compareUrl,
    ariaLabel: fromShort === null
      ? `View commit ${toShort}${where}`
      : `Compare ${fromShort} to ${toShort}${where}`,
  };
}

function isLong(text: string): boolean {
  return text.length > LONG_COMMENT_CHARACTERS || text.split('\n').length > LONG_COMMENT_LINES;
}

function openRound(round: Clarification, awaiting: AwaitingInputDetails, now: number): OpenRoundView {
  return {
    post: postView(awaiting),
    waited: formatWaiting(Math.max(0, now - Date.parse(round.askedAt))),
    reminder: reminderView(awaiting),
  };
}

function postView(awaiting: AwaitingInputDetails): PostView {
  switch (awaiting.postStatus) {
    case 'posted':
      return { status: 'posted', postedAt: awaiting.postedAt, commentUrl: awaiting.commentUrl };
    case 'failed':
      return {
        status: 'failed',
        reason: awaiting.postFailureReason ?? 'unknown reason',
        nextAttemptAt: awaiting.nextPostAttemptAt,
      };
    default:
      return { status: 'pending' };
  }
}

function reminderView(awaiting: AwaitingInputDetails): ReminderView | null {
  if (awaiting.reminderSentAt) {
    return { status: 'sent', at: awaiting.reminderSentAt };
  }
  if (awaiting.reminderDueAt) {
    return { status: 'due', at: awaiting.reminderDueAt };
  }
  return awaiting.postStatus === 'posted' ? null : { status: 'after_post' };
}

const LETTERS = 'abcd';

// An unknown type, or a recommended letter naming no option, shows the question alone, so a newer server
// never breaks the section.
export function questionView(q: InputQuestion): QuestionView {
  switch (q.$type) {
    case 'legacy':
      return { kind: 'legacy', question: q.question, context: q.context || null, recommendation: q.recommendation || null };
    case 'structured': {
      const options: OptionView[] = q.options.map((text, index) => {
        const letter = String.fromCharCode('a'.charCodeAt(0) + index);
        return { letter, text, recommended: letter === q.recommendedOption };
      });
      const recommended = options.find((o) => o.recommended);
      return recommended === undefined
        ? questionOnly(q.question)
        : {
          kind: 'structured',
          question: q.question,
          problem: q.problem,
          impact: q.impact,
          options,
          recommended,
          reason: q.recommendationReason,
          evidence: q.evidence,
        };
    }
    default:
      return questionOnly((q as { question?: string }).question ?? '');
  }
}

function questionOnly(question: string): QuestionView {
  return { kind: 'legacy', question, context: null, recommendation: null };
}

// An unknown ground from a newer server shows the decision without a ground label.
function decisionView(decision: ClarificationDecision): DecisionView {
  return {
    question: decision.question,
    ground: decision.ground,
    groundLabel: GROUND_LABELS[decision.ground] ?? null,
    blocks: decisionBlocks(decision.decision),
    evidence: decision.evidence,
  };
}

const TABLE_SEPARATOR_CELL = /^:?-+:?$/;

/**
 * Splits a decision's text into pre-wrapped lines and pipe tables, so a table triage wrote reads as one without a
 * Markdown renderer. A run of lines starting with `|` is a table only when its second row is a separator and every
 * row has the header's column count; anything else stays text.
 */
export function decisionBlocks(text: string): DecisionBlock[] {
  const blocks: DecisionBlock[] = [];
  let pending: string[] = [];
  const flushLines = (): void => {
    const lines = trimBlankLines(pending);
    if (lines.length > 0) {
      blocks.push({ type: 'lines', text: lines.join('\n') });
    }
    pending = [];
  };

  const lines = text.split('\n');
  let i = 0;
  while (i < lines.length) {
    if (!isPipeLine(lines[i])) {
      pending.push(lines[i]);
      i++;
      continue;
    }
    const start = i;
    while (i < lines.length && isPipeLine(lines[i])) {
      i++;
    }
    const run = lines.slice(start, i);
    const table = parseTable(run);
    if (table === null) {
      pending.push(...run);
    } else {
      flushLines();
      blocks.push(table);
    }
  }
  flushLines();
  return blocks;
}

function isPipeLine(line: string): boolean {
  return line.trimStart().startsWith('|');
}

function parseTable(run: readonly string[]): DecisionBlock | null {
  if (run.length < 2) {
    return null;
  }
  const [header, separator, ...rows] = run.map(cells);
  if (!separator.every((cell) => TABLE_SEPARATOR_CELL.test(cell))) {
    return null;
  }
  const consistent = [separator, ...rows].every((row) => row.length === header.length);
  return consistent ? { type: 'table', header, rows } : null;
}

function cells(line: string): string[] {
  const trimmed = line.trim();
  const inner = trimmed.length > 1 && trimmed.endsWith('|') ? trimmed.slice(1, -1) : trimmed.slice(1);
  return inner.split('|').map((cell) => cell.trim());
}

function trimBlankLines(lines: readonly string[]): readonly string[] {
  let start = 0;
  let end = lines.length;
  while (start < end && lines[start].trim() === '') {
    start++;
  }
  while (end > start && lines[end - 1].trim() === '') {
    end--;
  }
  return lines.slice(start, end);
}
