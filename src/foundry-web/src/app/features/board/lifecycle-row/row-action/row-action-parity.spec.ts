import { buildCallout } from '../../../issue-pane/pane-callout.model';
import { IssueDetail, IssueStateDetails, IssueSummary } from '../../../issues/issue.model';
import { IssueState } from '../../../../shared/utils/issue-state';
import { buildBoardBands, rowAction } from '../../board.model';

// Every issue state the pane offers actions for, with the facts that pick its actions.
interface ParityCase {
  readonly name: string;
  readonly state: IssueState;
  readonly summary?: Partial<IssueSummary>;
  readonly details?: Partial<IssueStateDetails>;
}

const CASES: readonly ParityCase[] = [
  { name: 'failed', state: 'failed' },
  { name: 'continuable failed', state: 'continuable_failed' },
  { name: 'revision failed', state: 'revision_failed' },
  { name: 'no changes', state: 'unchanged' },
  { name: 'branch diverged', state: 'branch_diverged' },
  { name: 'triage failed', state: 'triage_failed' },
  { name: 'publish failed', state: 'publish_failed' },
  { name: 'publish blocked', state: 'publish_blocked' },
  { name: 'review skipped', state: 'review', summary: { reviewOutcome: 'skipped' } },
  { name: 'merge skipped', state: 'review', summary: { mergeOutcome: 'skipped' } },
  { name: 'checks red', state: 'review', summary: { checksOutcome: 'still_failing' } },
  {
    name: 'questions posted',
    state: 'awaiting_input',
    summary: { questionPostStatus: 'posted' },
    details: { awaitingInput: awaitingInput('posted') },
  },
  {
    name: 'question post pending',
    state: 'awaiting_input',
    summary: { questionPostStatus: 'pending' },
    details: { awaitingInput: awaitingInput('pending') },
  },
  {
    name: 'question post failed',
    state: 'awaiting_input',
    summary: { questionPostStatus: 'failed' },
    details: { awaitingInput: awaitingInput('failed') },
  },
];

function awaitingInput(postStatus: 'pending' | 'posted' | 'failed'): IssueStateDetails['awaitingInput'] {
  return {
    questions: [],
    postStatus,
    postedAt: null,
    commentUrl: null,
    postFailureReason: null,
    nextPostAttemptAt: null,
    resumeTarget: 'fresh',
    reminderSentAt: null,
    reminderDueAt: null,
  };
}

function summary(c: ParityCase): IssueSummary {
  return {
    id: 'abc',
    issueNumber: 42,
    title: 'Enable dark mode',
    state: c.state,
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/api/issues/42',
    providerType: 'github',
    pullRequestUrl: 'https://github.com/acme/api/pull/9',
    stage: 'implement',
    needsAction: true,
    awaitsDispatch: false,
    waitsOnBlockers: false,
    codeStep: null,
    runKind: null,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    round: 1,
    ...c.summary,
  };
}

function detail(c: ParityCase): IssueDetail {
  return {
    ...summary(c),
    author: 'dev',
    labels: [],
    stateDetails: {
      workerRunId: 'run-1',
      branchName: 'feat/42-dark-mode',
      feedbackCutoffAt: null,
      failureReason: null,
      failedAt: null,
      completedAt: null,
      blockedBy: null,
      violations: null,
      transientRetry: null,
      ...c.details,
    },
    kind: { source: 'labelled', kind: 'feature', conflictingTriageKind: null },
    triage: null,
    clarifications: [],
  };
}

describe('row action and pane parity', () => {
  it.each(CASES.map((c) => [c.name, c] as const))('a row in %s runs only an action the pane offers', (_, c) => {
    // Arrange
    const issue = summary(c);
    const item = buildBoardBands([issue], [], []).needs[0];
    const paneActions = buildCallout(detail(c), null, issue, null).actions;

    // Act
    const action = rowAction(item);

    // Assert
    if (action?.kind === 'action') {
      expect(paneActions).toContain(action.action);
    }
  });

  it('covers every row action kind', () => {
    // Act
    const kinds = new Set(
      CASES.map((c) => rowAction(buildBoardBands([summary(c)], [], []).needs[0]))
        .filter((a) => a?.kind === 'action')
        .map((a) => (a?.kind === 'action' ? a.action : null)),
    );

    // Assert
    expect([...kinds].sort()).toEqual(['retry_issue', 'retry_post', 'retry_publish', 'retry_review', 'retry_triage']);
  });
});
