import { AwaitingInputDetails, Clarification, InputPart, InputQuestion, IssueDetail } from '../../issues/issue.model';
import { providerDisplayName } from '../../../shared/utils/provider.util';
import { formatWaiting } from '../../../shared/utils/format-waiting';

const SHORT_SHA_LENGTH = 7;
/** A comment longer than this many lines or characters starts collapsed with an expand control. */
const LONG_COMMENT_LINES = 6;
const LONG_COMMENT_CHARACTERS = 480;

export type RoundStatus = 'open' | 'answered' | 'unanswered';

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

export interface ClarificationRoundView {
  /** 1-based, oldest round first. */
  readonly number: number;
  readonly status: RoundStatus;
  readonly statusLabel: string;
  readonly questions: readonly InputQuestion[];
  readonly askedAt: string;
  readonly answeredAt: string | null;
  readonly input: readonly InputPartView[];
  /** Set only for the open round of an awaiting-input issue. */
  readonly open: OpenRoundView | null;
}

const STATUS_LABELS: Record<RoundStatus, string> = {
  open: 'Open',
  answered: 'Answered',
  unanswered: 'Continued without answer',
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
      questions: round.questions,
      askedAt: round.askedAt,
      answeredAt: round.answeredAt,
      input: (round.input ?? []).flatMap((part) => partView(part, providerName)),
      open: status === 'open' && awaiting !== null ? openRound(round, awaiting, now) : null,
    };
  });
}

function roundStatus(round: Clarification): RoundStatus {
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
