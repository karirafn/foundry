import { IssueSummary } from '../issues/issue.model';
import {
  CODE_STEP_LABELS,
  STAGES,
  TRIGGER_LABEL,
  buildBoardColumns,
  cellFor,
  codeStepTone,
  isRunning,
  needsReason,
} from './board.model';

function buildIssue(overrides: Partial<IssueSummary> = {}): IssueSummary {
  return {
    id: 'issue-1',
    issueNumber: 1,
    title: 'An issue',
    state: 'in_progress',
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/api/issues/1',
    repositoryEligibilityStatus: 'eligible',
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: false,
    codeStep: null,
    ...overrides,
  };
}

function queued(id: string, overrides: Partial<IssueSummary> = {}): IssueSummary {
  return buildIssue({
    id,
    state: 'queued',
    stage: 'implement',
    awaitsDispatch: true,
    ...overrides,
  });
}

describe('STAGES', () => {
  it('lists the stages in flow order', () => {
    expect(STAGES.map((s) => s.label)).toEqual(['Triage', 'Implement', 'Your review']);
  });

  it('gives the review stage no queue', () => {
    expect(STAGES.find((s) => s.id === 'awaiting_review')?.hasQueue).toBe(false);
  });
});

describe('TRIGGER_LABEL', () => {
  it('is the backend trigger label', () => {
    // Copied from WebhookRelevance.cs in the Monitoring module.
    expect(TRIGGER_LABEL).toBe('foundry');
  });
});

describe('cellFor', () => {
  it('puts a needs-action issue in the needs cell even when it also awaits dispatch', () => {
    expect(cellFor(buildIssue({ needsAction: true, awaitsDispatch: true }))).toBe('needs');
  });

  it('puts an issue awaiting dispatch in the queue cell', () => {
    expect(cellFor(buildIssue({ awaitsDispatch: true }))).toBe('queue');
  });

  it('puts any other issue in flow', () => {
    expect(cellFor(buildIssue())).toBe('flow');
  });
});

describe('buildBoardColumns', () => {
  it('places each issue in the column of its stage', () => {
    // Arrange
    const issues = [
      buildIssue({ id: 'a', stage: 'triage', state: 'triage_in_progress' }),
      buildIssue({ id: 'b', stage: 'awaiting_review', state: 'review', needsAction: true }),
    ];

    // Act
    const columns = buildBoardColumns(issues, []);

    // Assert
    expect(columns[0].flow.map((c) => c.issue.id)).toEqual(['a']);
    expect(columns[2].needs.map((c) => c.issue.id)).toEqual(['b']);
  });

  it('leaves out done issues and issues with an unknown stage', () => {
    // Arrange
    const issues = [buildIssue({ id: 'a', stage: 'done' }), buildIssue({ id: 'b', stage: null })];

    // Act
    const columns = buildBoardColumns(issues, []);

    // Assert
    expect(columns.flatMap((c) => [...c.queue, ...c.flow, ...c.needs])).toEqual([]);
  });

  it('counts every card in a column', () => {
    // Arrange
    const issues = [
      queued('q'),
      buildIssue({ id: 'f' }),
      buildIssue({ id: 'n', needsAction: true }),
    ];

    // Act
    const columns = buildBoardColumns(issues, ['q']);

    // Assert
    expect(columns[1].count).toBe(3);
  });

  it('keeps queued issues in server order with their global queue position', () => {
    // Arrange
    const issues = [
      queued('t1', { stage: 'triage', state: 'triage_queued' }),
      queued('i1'),
      queued('i2', { state: 'revision_queued' }),
    ];

    // Act
    const columns = buildBoardColumns(issues, ['i1', 't1', 'i2']);

    // Assert
    expect(columns[1].queue.map((c) => [c.issue.id, c.position])).toEqual([
      ['i1', 1],
      ['i2', 3],
    ]);
    expect(columns[0].queue.map((c) => c.position)).toEqual([2]);
  });

  it('lists positionless eligible issues after the queued ones, then ineligible ones', () => {
    // Arrange
    const issues = [
      queued('blocked', { state: 'blocked' }),
      queued('ineligible', { repositoryEligibilityStatus: 'ineligible' }),
      queued('unreachable', { repositoryEligibilityStatus: 'unreachable' }),
      queued('first'),
    ];

    // Act
    const columns = buildBoardColumns(issues, ['first']);

    // Assert
    expect(columns[1].queue.map((c) => c.issue.id)).toEqual([
      'first',
      'blocked',
      'ineligible',
      'unreachable',
    ]);
  });

  it('marks ineligible queued issues and gives them no position', () => {
    // Act
    const [card] = buildBoardColumns(
      [queued('x', { repositoryEligibilityStatus: 'ineligible' })],
      [],
    )[1].queue;

    // Assert
    expect(card.ineligible).toBe(true);
    expect(card.position).toBeNull();
  });

  it('marks blocked and detected queue cards', () => {
    // Arrange
    const issues = [
      queued('b', { state: 'blocked' }),
      queued('d', { state: 'detected', stage: 'triage' }),
    ];

    // Act
    const columns = buildBoardColumns(issues, []);

    // Assert
    expect(columns[1].queue[0].queueMarker).toBe('Blocked');
    expect(columns[0].queue[0].queueMarker).toBe('Detected');
  });

  it('gives a card awaiting dispatch its stage as the next role', () => {
    // Act
    const columns = buildBoardColumns(
      [queued('t', { stage: 'triage', state: 'triage_queued' })],
      ['t'],
    );

    // Assert
    expect(columns[0].queue[0].nextRole).toBe('triage');
  });

  it('gives a card in flow no next role', () => {
    expect(buildBoardColumns([buildIssue()], [])[1].flow[0].nextRole).toBeNull();
  });
});

describe('needsReason', () => {
  it('is null for an issue that needs no action', () => {
    expect(needsReason(buildIssue())).toBeNull();
  });

  it('reads red checks for a review whose checks are still failing', () => {
    expect(
      needsReason(
        buildIssue({ state: 'review', needsAction: true, checksOutcome: 'still_failing' }),
      ),
    ).toEqual({ label: 'Checks red', tone: 'error' });
  });

  it('reads a ready pull request for a GitHub review', () => {
    expect(
      needsReason(buildIssue({ state: 'review', needsAction: true, providerType: 'github' })),
    ).toEqual({
      label: 'PR ready',
      tone: 'ready',
    });
  });

  it('reads a ready merge request for a GitLab review', () => {
    expect(
      needsReason(buildIssue({ state: 'review', needsAction: true, providerType: 'gitlab' }))
        ?.label,
    ).toBe('MR ready');
  });

  it.each([
    ['awaiting_input', 'Needs answers', 'warning'],
    ['unchanged', 'No changes', 'neutral'],
    ['failed', 'Failed', 'error'],
    ['continuable_failed', 'Failed', 'error'],
    ['revision_failed', 'Failed', 'error'],
    ['triage_failed', 'Failed', 'error'],
    ['publish_failed', 'Publish failed', 'error'],
    ['publish_blocked', 'Publish blocked', 'error'],
    ['branch_diverged', 'Branch diverged', 'warning'],
  ] as const)('reads %s as %s', (state, label, tone) => {
    expect(needsReason(buildIssue({ state, needsAction: true }))).toEqual({ label, tone });
  });
});

describe('code steps', () => {
  it('labels every code step', () => {
    expect(CODE_STEP_LABELS).toEqual({
      publishing: 'Publishing…',
      publish_retrying: 'Retrying publish…',
      rebasing: 'Rebasing…',
      checks: 'Checks running…',
    });
  });

  it('reads a publish retry in the warning tone', () => {
    expect(codeStepTone('publish_retrying')).toBe('warning');
  });

  it('reads other steps in the working tone', () => {
    expect(codeStepTone('checks')).toBe('working');
  });
});

describe('isRunning', () => {
  it('is true while a worker runs', () => {
    expect(isRunning(buildIssue({ state: 'in_progress' }))).toBe(true);
  });

  it('is false while the run publishes', () => {
    expect(isRunning(buildIssue({ state: 'in_progress', publishPhase: 'publishing' }))).toBe(false);
  });

  it('is false for a state with no worker', () => {
    expect(isRunning(buildIssue({ state: 'checks_pending' }))).toBe(false);
  });
});
