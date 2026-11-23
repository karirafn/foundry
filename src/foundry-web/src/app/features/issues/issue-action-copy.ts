import type { ConfirmOptions } from '../../shared/components/confirm-dialog/confirm-dialog.service';
import { providerDisplayName } from '../../shared/utils/provider.util';
import type { IssueActionKind, IssueActionRequest } from './issue-actions.service';

const RETRIAGE_MESSAGE =
  'The issue leaves the queue and a triage worker reads it again. If it needs more detail, the questions are posted on the issue and it waits for a reply.';
const RETRIAGE_BLOCKED_SUFFIX = ' While its blockers are open it waits untriaged, and triage runs once they close.';
const CONTINUE_WITHOUT_ANSWER_MESSAGE =
  'The open questions close unanswered and the work resumes; the worker is told to use its judgment and cannot ask again in that run.';
const CONTINUE_WITHOUT_ANSWER_TRIAGE_MESSAGE =
  'The open questions close unanswered and the issue is queued without a readiness check, as Skip triage does. If it has open blockers, it waits as Blocked first.';
// On SIGTERM the worker bundles its work and the Publisher pushes it.
const IMPLEMENT_STOP_MESSAGE = "Foundry publishes the run's commits to the branch; Retry continues from them.";
const BRANCH_FALLBACK = 'the issue branch';

const SUCCESS_MESSAGES: Readonly<Record<IssueActionKind, string | null>> = {
  retry_issue: 'Retry queued. Issue status is updating.',
  retry_fresh: 'Branch discarded. Issue queued fresh; status is updating.',
  retry_triage: 'Triage queued again. Issue status is updating.',
  skip_triage: 'Triage skipped. Issue status is updating.',
  retriage: 'Re-triage queued. Issue status is updating.',
  continue_without_answer: 'Continuing without an answer. Issue status is updating.',
  retry_post: 'Posting the questions again. Issue status is updating.',
  retry_review: 'Review queued again. Issue status is updating.',
  // The stopped run's callout says what happened.
  stop_worker: null,
  retry_publish: 'Publish retry started. Issue status is updating.',
  discard_run: 'Run discarded. Issue status is updating.',
  confirm_false_positives: 'Findings confirmed. Publishing restarted; issue status is updating.',
};

function possibleSecrets(count: number): string {
  if (count === 0) {
    return 'possible secrets';
  }
  return count === 1 ? '1 possible secret' : `${count} possible secrets`;
}

function providerName(providerType: string): string {
  return providerDisplayName(providerType) || 'the provider';
}

/** The dialog an action asks before it runs, or null for one that runs straight away. */
export function actionConfirmation(action: IssueActionRequest): ConfirmOptions | null {
  switch (action.kind) {
    case 'retry_issue':
    case 'retry_triage':
    case 'retry_post':
    case 'retry_review':
    // Confirmed by its caller's dialog, which lists the findings: see confirmFalsePositivesCopy.
    case 'confirm_false_positives':
      return null;
    case 'stop_worker':
      return {
        title: action.triageRun ? 'Stop the triage worker?' : 'Stop the worker?',
        message: action.triageRun
          ? 'The issue moves to Triage failed, where you can retry or skip triage.'
          : IMPLEMENT_STOP_MESSAGE,
        confirmLabel: 'Stop worker',
        cancelLabel: 'Keep running',
        destructive: true,
      };
    // Not destructive, but pushing triggers CI with repository secrets, so confirm first.
    case 'retry_publish':
      return {
        title: 'Retry publishing?',
        message: `Foundry pushes the run's commits to ${action.branchName ?? BRANCH_FALLBACK} and opens or updates the pull request. CI runs on the push.`,
        confirmLabel: 'Retry publish',
        cancelLabel: 'Not now',
        destructive: false,
      };
    case 'discard_run':
      return {
        title: 'Discard this run?',
        message: "The run's commits are deleted from this host and nothing is pushed. The issue moves to Failed, where "
          + `Retry issue starts a new worker from the branch on ${providerName(action.providerType)}. This cannot be undone.`,
        confirmLabel: 'Discard run',
        cancelLabel: 'Keep run',
        destructive: true,
      };
    case 'retry_fresh':
      return {
        title: 'Retry from a fresh branch?',
        message: 'The branch and all of its commits will be deleted on the provider, and any open pull request for it closed. The next worker starts over from the default branch. This cannot be undone.',
        confirmLabel: 'Discard branch and retry',
        cancelLabel: 'Keep branch',
        destructive: true,
      };
    case 'skip_triage':
      return {
        title: `Skip triage for #${action.issueNumber}?`,
        message: 'The issue is queued without a readiness check. If it has open blockers, it waits as Blocked first. Its kind stays as it is now.',
        confirmLabel: 'Skip triage',
        cancelLabel: 'Cancel',
        destructive: false,
      };
    case 'retriage':
      return {
        title: `Triage #${action.issueNumber} again?`,
        message: action.blocked ? RETRIAGE_MESSAGE + RETRIAGE_BLOCKED_SUFFIX : RETRIAGE_MESSAGE,
        confirmLabel: 'Re-triage',
        cancelLabel: 'Cancel',
        destructive: false,
      };
    case 'continue_without_answer':
      return {
        title: `Continue #${action.issueNumber} without an answer?`,
        message: action.resumesTriage ? CONTINUE_WITHOUT_ANSWER_TRIAGE_MESSAGE : CONTINUE_WITHOUT_ANSWER_MESSAGE,
        confirmLabel: 'Continue without answer',
        cancelLabel: 'Keep waiting',
        destructive: false,
      };
  }
}

/** The announcement once an action succeeded; null for one that announces nothing. */
export function actionSuccessMessage(kind: IssueActionKind): string | null {
  return SUCCESS_MESSAGES[kind];
}

/**
 * The copy of the Confirm false positives dialog, whose body lists the findings and an
 * acknowledgement, so it is rendered by the caller rather than through ConfirmDialogService.
 */
export function confirmFalsePositivesCopy(
  findingCount: number,
  branchName: string | null,
  providerType: string,
): Required<ConfirmOptions> {
  return {
    title: `Publish with ${possibleSecrets(findingCount)}?`,
    message: `Foundry pushes the run's commits to ${branchName ?? BRANCH_FALLBACK}, including the content these findings flagged. Once pushed, `
      + `it stays in the repository's history on ${providerName(providerType)}, and CI runs on the push. This cannot be undone.`,
    confirmLabel: 'Confirm and publish',
    cancelLabel: 'Keep blocked',
    destructive: true,
  };
}
