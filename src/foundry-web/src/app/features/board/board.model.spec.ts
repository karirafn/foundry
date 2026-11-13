import { IssueSummary } from '../issues/issue.model';
import { RepositorySummary } from '../settings/repositories/repository.model';
import {
  CODE_STEP_LABELS,
  STAGE_LABELS,
  STAGE_ORDER,
  TRIGGER_LABEL,
  bandFor,
  buildBoardBands,
  codeStepTone,
  isRunning,
  needsReason,
  repoName,
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
    waitsOnBlockers: false,
    codeStep: null,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
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

function repository(overrides: Partial<RepositorySummary> = {}): RepositorySummary {
  return {
    id: 'repo-1',
    slug: 'acme/api',
    accountId: 'account-1',
    accountName: 'acme',
    providerType: 'github',
    position: 0,
    pollIntervalSeconds: null,
    effectivePollIntervalSeconds: 60,
    pollIntervalIsDefault: true,
    isActive: true,
    maxConcurrentWorkers: 1,
    rebaseAfterRun: true,
    lastPolledAt: null,
    eligibility: { status: 'ineligible', violations: [], reason: null },
    ...overrides,
  };
}

function ids(items: readonly { issue: IssueSummary }[]): string[] {
  return items.map((i) => i.issue.id);
}

describe('STAGE_LABELS', () => {
  it('names the stages in sentence case', () => {
    expect(STAGE_ORDER.map((s) => STAGE_LABELS[s])).toEqual(['Triage', 'Implement', 'Your review']);
  });
});

describe('TRIGGER_LABEL', () => {
  it('is the backend trigger label', () => {
    // Copied from WebhookRelevance.cs in the Monitoring module.
    expect(TRIGGER_LABEL).toBe('foundry');
  });
});

describe('bandFor', () => {
  it('puts a needs-action issue in Needs you before every other rule', () => {
    expect(
      bandFor(buildIssue({ needsAction: true, awaitsDispatch: true, waitsOnBlockers: true })),
    ).toBe('needs');
  });

  it.each(['in_progress', 'revision_in_progress', 'triage_in_progress'] as const)(
    'puts a running %s issue with a worker card',
    (state) => {
      expect(bandFor(buildIssue({ state }))).toBe('workers');
    },
  );

  it('puts a publishing run with the steps, not the workers', () => {
    expect(
      bandFor(
        buildIssue({ state: 'in_progress', publishPhase: 'publishing', codeStep: 'publishing' }),
      ),
    ).toBe('steps');
  });

  it('puts an issue with a code step with the steps', () => {
    expect(bandFor(buildIssue({ state: 'checks_pending', codeStep: 'checks' }))).toBe('steps');
  });

  it('puts an issue waiting on blockers in Blocked even though it awaits dispatch', () => {
    expect(
      bandFor(buildIssue({ state: 'blocked', awaitsDispatch: true, waitsOnBlockers: true })),
    ).toBe('blocked');
  });

  it('puts an issue awaiting dispatch in the Pipeline', () => {
    expect(bandFor(buildIssue({ state: 'queued', awaitsDispatch: true }))).toBe('pipeline');
  });

  it('places an issue no rule matches nowhere', () => {
    expect(bandFor(buildIssue({ state: 'checks_pending' }))).toBeNull();
  });
});

describe('buildBoardBands', () => {
  it('leaves out done issues silently', () => {
    // Act
    const bands = buildBoardBands([buildIssue({ stage: 'done', state: 'completed' })], [], []);

    // Assert
    expect(bands.unplaced).toEqual([]);
    expect(bands.workers).toEqual([]);
  });

  it('reports issues with an unknown stage or no matching rule as unplaced', () => {
    // Arrange
    const issues = [
      buildIssue({ id: 'a', stage: null }),
      buildIssue({ id: 'b', state: 'checks_pending' }),
    ];

    // Act
    const bands = buildBoardBands(issues, [], []);

    // Assert
    expect(bands.unplaced.map((i) => i.id)).toEqual(['a', 'b']);
  });

  it('orders needs by tone, then longest waiting, then issue number', () => {
    // Arrange
    const issues = [
      buildIssue({
        id: 'ready',
        issueNumber: 1,
        state: 'review',
        needsAction: true,
        enteredStateAt: '2026-01-01T00:00:00Z',
      }),
      buildIssue({
        id: 'neutral',
        issueNumber: 2,
        state: 'unchanged',
        needsAction: true,
        enteredStateAt: '2026-01-01T00:00:00Z',
      }),
      buildIssue({
        id: 'error-new',
        issueNumber: 3,
        state: 'failed',
        needsAction: true,
        enteredStateAt: '2026-01-03T00:00:00Z',
      }),
      buildIssue({
        id: 'warning',
        issueNumber: 4,
        state: 'awaiting_input',
        needsAction: true,
        enteredStateAt: '2026-01-01T00:00:00Z',
      }),
      buildIssue({
        id: 'error-old-9',
        issueNumber: 9,
        state: 'failed',
        needsAction: true,
        enteredStateAt: '2026-01-02T00:00:00Z',
      }),
      buildIssue({
        id: 'error-old-5',
        issueNumber: 5,
        state: 'publish_failed',
        needsAction: true,
        enteredStateAt: '2026-01-02T00:00:00Z',
      }),
    ];

    // Act
    const bands = buildBoardBands(issues, [], []);

    // Assert
    expect(ids(bands.needs)).toEqual([
      'error-old-5',
      'error-old-9',
      'error-new',
      'warning',
      'ready',
      'neutral',
    ]);
  });

  it('keeps every needs item', () => {
    // Arrange
    const issues = Array.from({ length: 25 }, (_, i) =>
      buildIssue({ id: `n${i}`, issueNumber: i, state: 'failed', needsAction: true }),
    );

    // Act
    const bands = buildBoardBands(issues, [], []);

    // Assert
    expect(bands.needs).toHaveLength(25);
  });

  it('orders workers by when they started running, longest first', () => {
    // Arrange
    const issues = [
      buildIssue({ id: 'late', enteredStateAt: '2026-01-02T00:00:00Z' }),
      buildIssue({ id: 'early', enteredStateAt: '2026-01-01T00:00:00Z' }),
    ];

    // Act
    const bands = buildBoardBands(issues, [], []);

    // Assert
    expect(ids(bands.workers)).toEqual(['early', 'late']);
  });

  it('gives a triage worker the triage role and others the implement role', () => {
    // Arrange
    const issues = [
      buildIssue({ id: 't', stage: 'triage', state: 'triage_in_progress' }),
      buildIssue({ id: 'r', state: 'revision_in_progress' }),
    ];

    // Act
    const bands = buildBoardBands(issues, [], []);

    // Assert
    expect(bands.workers.map((w) => [w.issue.id, w.role])).toEqual([
      ['t', 'triage'],
      ['r', 'implement'],
    ]);
  });

  it('lists the pipeline in global dispatch order across stages', () => {
    // Arrange
    const issues = [
      queued('i1'),
      queued('t1', { stage: 'triage', state: 'triage_queued' }),
      queued('i2', { state: 'revision_queued' }),
    ];

    // Act
    const bands = buildBoardBands(issues, ['t1', 'i1', 'i2'], []);

    // Assert
    expect(bands.pipeline.map((p) => [p.issue.id, p.slot])).toEqual([
      ['t1', { kind: 'position', position: 1 }],
      ['i1', { kind: 'position', position: 2 }],
      ['i2', { kind: 'position', position: 3 }],
    ]);
  });

  it('lists detected rows after the positioned ones, then held rows, each in server order', () => {
    // Arrange
    const issues = [
      queued('ineligible', { repositoryEligibilityStatus: 'ineligible' }),
      queued('detected-1', { state: 'detected', stage: 'triage' }),
      queued('unreachable', { repositoryEligibilityStatus: 'unreachable' }),
      queued('first'),
      queued('detected-2', { state: 'detected', stage: 'triage' }),
    ];

    // Act
    const bands = buildBoardBands(issues, ['first'], []);

    // Assert
    expect(bands.pipeline.map((p) => [p.issue.id, p.slot.kind])).toEqual([
      ['first', 'position'],
      ['detected-1', 'detected'],
      ['detected-2', 'detected'],
      ['ineligible', 'ineligible'],
      ['unreachable', 'ineligible'],
    ]);
  });

  it('gives a pipeline row its next role', () => {
    // Act
    const bands = buildBoardBands(
      [queued('t', { stage: 'triage', state: 'triage_queued' })],
      ['t'],
      [],
    );

    // Assert
    expect(bands.pipeline[0].role).toBe('triage');
  });

  it('lists blocked issues in server order', () => {
    // Arrange
    const issues = [
      queued('b2', { state: 'blocked', waitsOnBlockers: true }),
      queued('b1', { state: 'triage_blocked', stage: 'triage', waitsOnBlockers: true }),
    ];

    // Act
    const bands = buildBoardBands(issues, [], []);

    // Assert
    expect(bands.blocked.map((b) => [b.issue.id, b.role])).toEqual([
      ['b2', 'implement'],
      ['b1', 'triage'],
    ]);
  });

  describe('repository alerts', () => {
    it('raises one alert per ineligible repository holding queued issues', () => {
      // Arrange
      const issues = [
        queued('a', { repositoryEligibilityStatus: 'ineligible', repositoryId: 'repo-1' }),
        queued('b', { repositoryEligibilityStatus: 'ineligible', repositoryId: 'repo-1' }),
        queued('c', {
          repositoryEligibilityStatus: 'ineligible',
          repositoryId: 'repo-2',
          repositorySlug: 'acme/web',
        }),
      ];
      const repositories = [
        repository({
          id: 'repo-1',
          slug: 'acme/api',
          eligibility: {
            status: 'ineligible',
            violations: [
              { rule: 'branch-protection:a', description: 'Require pull requests' },
              { rule: 'branch-protection:b', description: 'Block force pushes' },
              { rule: 'branch-protection:c', description: 'Require checks' },
            ],
            reason: null,
          },
        }),
        repository({ id: 'repo-2', slug: 'acme/web' }),
      ];

      // Act
      const { alerts } = buildBoardBands(issues, [], repositories);

      // Assert
      expect(alerts).toEqual([
        {
          repositoryId: 'repo-1',
          slug: 'acme/api',
          heldCount: 2,
          firstViolation: 'Require pull requests',
          moreViolations: 2,
        },
        {
          repositoryId: 'repo-2',
          slug: 'acme/web',
          heldCount: 1,
          firstViolation: null,
          moreViolations: 0,
        },
      ]);
    });

    it('joins issues to repositories by id, never by slug', () => {
      // Arrange
      const issues = [
        queued('a', {
          repositoryEligibilityStatus: 'ineligible',
          repositoryId: 'repo-2',
          repositorySlug: 'acme/api',
        }),
      ];
      const repositories = [
        repository({
          id: 'repo-1',
          slug: 'acme/api',
          eligibility: {
            status: 'ineligible',
            violations: [{ rule: 'x', description: 'Wrong one' }],
            reason: null,
          },
        }),
        repository({
          id: 'repo-2',
          slug: 'acme/api',
          eligibility: {
            status: 'ineligible',
            violations: [{ rule: 'y', description: 'Right one' }],
            reason: null,
          },
        }),
      ];

      // Act
      const { alerts } = buildBoardBands(issues, [], repositories);

      // Assert
      expect(alerts.map((a) => [a.repositoryId, a.firstViolation])).toEqual([
        ['repo-2', 'Right one'],
      ]);
    });

    it('raises no alert for an unreachable repository', () => {
      // Act
      const { alerts } = buildBoardBands(
        [queued('a', { repositoryEligibilityStatus: 'unreachable' })],
        [],
        [repository()],
      );

      // Assert
      expect(alerts).toEqual([]);
    });

    it('raises no alert for an ineligible repository with nothing queued', () => {
      // Act
      const { alerts } = buildBoardBands(
        [buildIssue({ repositoryEligibilityStatus: 'ineligible' })],
        [],
        [repository()],
      );

      // Assert
      expect(alerts).toEqual([]);
    });
  });
});

describe('needs explanations', () => {
  function explanation(overrides: Partial<IssueSummary>): string {
    return buildBoardBands([buildIssue({ needsAction: true, ...overrides })], [], []).needs[0]
      .explanation;
  }

  it('names the role and failure classification of a failed run', () => {
    expect(explanation({ state: 'failed', failureClassification: 'timed_out' })).toBe(
      'Implement run failed: timed out',
    );
  });

  it('names the triage role for a failed triage', () => {
    expect(
      explanation({
        state: 'triage_failed',
        stage: 'triage',
        failureClassification: 'non_zero_exit',
      }),
    ).toBe('Triage run failed: non-zero exit');
  });

  it('falls back when a failed run has no classification', () => {
    expect(explanation({ state: 'failed' })).toBe('The run failed');
  });

  it.each([
    [{ state: 'review', checksOutcome: 'still_failing' }, 'Checks failed'],
    [{ state: 'publish_failed' }, "Foundry couldn't push the run's commits"],
    [{ state: 'publish_blocked' }, 'A secret was found, so nothing was pushed'],
    [{ state: 'awaiting_input' }, 'Questions are waiting for your answers'],
    [{ state: 'branch_diverged' }, "The branch moved; the run's work is on a side branch"],
    [{ state: 'review' }, 'Ready for your review'],
    [{ state: 'unchanged' }, 'The run made no changes'],
    [{ state: 'rebase_pending' }, "Open to see what's needed"],
  ] as [Partial<IssueSummary>, string][])('explains %o as %s', (overrides, text) => {
    expect(explanation(overrides)).toBe(text);
  });
});

describe('repoName', () => {
  it('is the last segment of the slug', () => {
    expect(repoName('acme/team/isal-monitoring')).toBe('isal-monitoring');
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
