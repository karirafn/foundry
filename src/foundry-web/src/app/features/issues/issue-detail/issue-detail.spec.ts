import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { IssueDetailComponent } from './issue-detail';
import { IssueDetail, IssueStateDetails } from '../issue.model';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { WorkerRunService } from '../../workers/worker-run.service';
import { WorkerRunDetail } from '../../workers/worker-run.model';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { provideFakeHubs } from '../../../core/testing/fake-hub-transport';

const mockStateDetails: IssueStateDetails = {
  workerRunId: null,
  branchName: 'feat/dark-mode',
  feedbackCutoffAt: null,
  failureReason: null,
  failedAt: null,
  completedAt: '2026-02-01T12:00:00Z',
  blockedBy: null,
  violations: null,
  transientRetry: null,
};

const mockDetail: IssueDetail = {
  id: 'abc123',
  issueNumber: 42,
  title: 'Enable dark mode',
  state: 'completed',
  repositorySlug: 'owner/repo',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/owner/repo/issues/42',
  stage: 'done',
  needsAction: false,
  awaitsDispatch: false,
  codeStep: null,
  author: 'dev',
  labels: ['enhancement', 'ui'],
  stateDetails: mockStateDetails,
  kind: { source: 'labelled', kind: 'feature', conflictingTriageKind: null },
  triage: null,
};

function runDetail(overrides: Partial<WorkerRunDetail> = {}): WorkerRunDetail {
  return {
    workerRunId: 'run-1',
    issueId: 'abc123',
    role: 'implement',
    state: 'completed',
    failureCategory: null,
    failureSummary: null,
    resultText: null,
    subtype: null,
    isError: null,
    durationMs: null,
    numTurns: null,
    totalCostUsd: null,
    inputTokens: null,
    outputTokens: null,
    lastActivityAt: null,
    hasStoredLog: false,
    cancelRequestedAt: null,
    publish: null,
    publishRetry: null,
    discardsAt: null,
    secretFindings: [],
    revertedFiles: [],
    ...overrides,
  };
}

function issue(state: string, overrides: Partial<IssueDetail> = {}): IssueDetail {
  return { ...mockDetail, state: state as IssueDetail['state'], ...overrides };
}

function setup(detail: IssueDetail, run: WorkerRunDetail | null = null, getLog = vi.fn(() => of<string | null>(null))) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [IssueDetailComponent],
    providers: [
      WorkerSignalRService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: IssueSignalRService, useValue: { on: () => {}, onReconnected: () => {} } },
      { provide: WorkerRunService, useValue: { getDetail: () => of(null), getLog } },
      provideFakeHubs(),
    ],
  });
  const fixture = TestBed.createComponent(IssueDetailComponent);
  fixture.componentRef.setInput('detail', detail);
  fixture.componentRef.setInput('run', run);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement, getLog };
}

function text(node: Element | null | undefined): string {
  return (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

describe('IssueDetailComponent', () => {
  it('names its region by the issue number', () => {
    // Act
    const { el } = setup(mockDetail);

    // Assert
    expect(el.querySelector('[role="region"]')?.getAttribute('aria-label')).toBe('Issue details for #42');
  });

  it('renders each label as a pill', () => {
    // Act
    const { el } = setup(mockDetail);

    // Assert
    expect(Array.from(el.querySelectorAll('.issue-detail__label-pill')).map(text)).toEqual(['enhancement', 'ui']);
  });

  it('shows the branch, and no Branch row without one', () => {
    // Act
    const withBranch = setup(mockDetail).el;
    const withoutBranch = setup(issue('completed', { stateDetails: { ...mockStateDetails, branchName: null } })).el;

    // Assert
    expect(text(withBranch.querySelector('.issue-detail__branch'))).toBe('feat/dark-mode');
    expect(withoutBranch.querySelector('.issue-detail__branch')).toBeNull();
  });

  it('shows the author when the issue has no state details', () => {
    // Act
    const { el } = setup(issue('queued', { stateDetails: null }));

    // Assert
    const author = el.querySelector('.issue-detail__fields--author');
    expect(text(author?.querySelector('.issue-detail__field-key'))).toBe('Author');
    expect(text(author?.querySelector('.issue-detail__field-value'))).toBe('dev');
  });

  // The callout, actions and their confirms moved to the pane's status (#862).
  it('renders no callout, action button or confirm dialog', () => {
    // Act
    const { el } = setup(
      issue('failed', {
        stateDetails: {
          ...mockStateDetails,
          failureReason: 'Exit code 1',
          transientRetry: { attemptNumber: 1, maxAttempts: 3, isExhausted: false, nextAttemptDueAt: null },
        },
      }),
    );

    // Assert
    expect(el.querySelector('button')).toBeNull();
    expect(el.querySelector('fd-confirm-dialog')).toBeNull();
    expect(el.querySelector('[role="status"]')).toBeNull();
    expect(el.textContent).not.toContain('Exit code 1');
    expect(el.textContent).not.toContain('Automatic retry');
  });

  it.each([
    ['up_to_date', null, 'Up to date'],
    ['skipped', 'timeout', 'Skipped (timeout)'],
    ['conflict', null, 'Conflict'],
  ])('shows a Rebase row for %s outcome (reason %s) as "%s"', (outcome, reason, expected) => {
    // Act
    const { el } = setup(
      issue('review', { stateDetails: { ...mockStateDetails, rebaseOutcome: outcome, rebaseSkippedReason: reason } }),
    );

    // Assert
    expect(text(el.querySelector('.issue-detail__rebase .issue-detail__field-value'))).toBe(expected);
  });

  describe('kind', () => {
    it('renders the Kind field first in the fields grid', () => {
      // Act
      const { el } = setup(issue('queued', { stateDetails: null }));

      // Assert
      const first = el.querySelector('.issue-detail__fields > .issue-detail__field');
      expect(first?.classList.contains('issue-detail__field--kind')).toBe(true);
    });

    it.each([
      ['labelled', 'bug', 'Bug', 'From label'],
      ['triaged', 'feature', 'Feature', 'From triage'],
      ['unclassified', null, 'Unclassified', 'No kind label, and triage has not classified it'],
    ] as const)('shows a %s %s kind as "%s" with the note "%s"', (source, kind, label, note) => {
      // Act
      const { el } = setup(issue('queued', { kind: { source, kind, conflictingTriageKind: null } }));

      // Assert
      expect(text(el.querySelector('.issue-detail__kind-value'))).toBe(label);
      expect(text(el.querySelector('.issue-detail__kind-source'))).toBe(note);
    });

    it('shows the Triage differs line when triage judged another kind', () => {
      // Act
      const { el } = setup(issue('queued', { kind: { source: 'labelled', kind: 'bug', conflictingTriageKind: 'refactor' } }));

      // Assert
      const line = el.querySelector('.issue-detail__kind-differs');
      expect(text(line?.querySelector('.issue-detail__chip'))).toBe('Triage differs');
      expect(text(line)).toContain('Triage read it as a refactor. The label decides; nothing changes.');
    });
  });

  describe('blocked by', () => {
    const blockers = { ...mockStateDetails, blockedBy: [{ issueNumber: 12, reason: null }, { issueNumber: 14, reason: 'waits on the schema' }] };

    it('lists each blocker with its number and triage reason, named by the Blocked By key', () => {
      // Act
      const { el } = setup(issue('blocked', { stateDetails: blockers }));

      // Assert
      const list = el.querySelector('ul.issue-detail__blockers');
      expect(Array.from(list?.querySelectorAll('.issue-detail__blocker-number') ?? []).map(text)).toEqual(['#12', '#14']);
      expect(Array.from(list?.querySelectorAll('.issue-detail__blocker-reason') ?? []).map(text)).toEqual(['Triage: waits on the schema']);
      const labelId = list?.getAttribute('aria-labelledby');
      expect(text(el.querySelector(`[id="${labelId}"]`))).toBe('Blocked By');
    });

    it('gives each component instance its own blockers label id', () => {
      // Act
      const first = setup(issue('blocked', { stateDetails: blockers })).el.querySelector('ul')?.getAttribute('aria-labelledby');
      const second = setup(issue('blocked', { stateDetails: blockers })).el.querySelector('ul')?.getAttribute('aria-labelledby');

      // Assert
      expect(first).not.toBe(second);
    });
  });

  describe('triage fact', () => {
    const verdict = (overrides: Partial<NonNullable<IssueDetail['triage']>> = {}): NonNullable<IssueDetail['triage']> => ({
      outcome: 'ready',
      kind: 'feature',
      questions: [],
      decidedAt: '2026-11-06T14:14:03Z',
      ...overrides,
    });

    it('shows a Ready verdict with its kind and decision time', () => {
      // Act
      const { el } = setup(issue('queued', { stateDetails: null, triage: verdict() }));

      // Assert
      const fact = el.querySelector('.issue-detail__triage-fact');
      expect(text(fact?.querySelector('.issue-detail__field-key'))).toBe('Triage');
      expect(text(fact?.querySelector('.issue-detail__chip'))).toBe('Ready');
      expect(fact?.querySelector('.issue-detail__chip--success')).not.toBeNull();
      expect(text(fact)).toContain('Feature');
      expect(text(fact)).toMatch(/Triaged Nov 6, 2026/);
    });

    it.each([
      [1, '1 question asked'],
      [3, '3 questions asked'],
    ])('shows a Needs input verdict with %i question(s) asked', (count, phrase) => {
      // Act
      const { el } = setup(
        issue('in_progress', { triage: verdict({ outcome: 'needs_input', questions: Array.from({ length: count }, (_, i) => `Q${i}`) }) }),
      );

      // Assert
      const fact = el.querySelector('.issue-detail__triage-fact');
      expect(text(fact?.querySelector('.issue-detail__chip'))).toBe('Needs input');
      expect(text(fact)).toContain(phrase);
    });

    it('explains a Blocked verdict as depending on open issues, with a neutral chip', () => {
      // Act
      const { el } = setup(issue('queued', { triage: verdict({ outcome: 'blocked' }) }));

      // Assert
      const fact = el.querySelector('.issue-detail__triage-fact');
      expect(fact?.querySelector('.issue-detail__chip--neutral')).not.toBeNull();
      expect(text(fact)).toContain('Triage found open issues this depends on.');
    });

    it('falls back to the raw outcome for an outcome it has no label for', () => {
      // Act
      const { el } = setup(issue('queued', { triage: verdict({ outcome: 'future_outcome' as 'ready' }) }));

      // Assert
      expect(text(el.querySelector('.issue-detail__triage-fact .issue-detail__chip'))).toBe('future_outcome');
    });

    it.each(['queued', 'blocked'])('says a %s issue without a verdict was not triaged', (state) => {
      // Act
      const { el } = setup(issue(state, { stateDetails: null }));

      // Assert
      expect(text(el.querySelector('.issue-detail__triage-fact .issue-detail__triage-message'))).toBe(
        'Not triaged. This issue was queued without a readiness check.',
      );
    });

    it.each(['triage_queued', 'triage_blocked', 'triage_in_progress', 'triage_failed'])(
      'hides a stale verdict while the issue is %s',
      (state) => {
        // Act
        const { el } = setup(issue(state, { triage: verdict() }));

        // Assert
        expect(el.querySelector('.issue-detail__triage-fact')).toBeNull();
      },
    );

    it('hides a stale verdict while triage waits for answers', () => {
      // Act
      const { el } = setup(
        issue('awaiting_input', {
          triage: verdict(),
          stateDetails: {
            ...mockStateDetails,
            awaitingInput: {
              questions: [],
              postStatus: 'pending',
              postedAt: null,
              commentUrl: null,
              postFailureReason: null,
              nextPostAttemptAt: null,
              resumeTarget: 'retriage',
            },
          },
        }),
      );

      // Assert
      expect(el.querySelector('.issue-detail__triage-fact')).toBeNull();
    });

    it.each(['completed', 'review', 'failed'])('shows no triage fact for %s with no verdict', (state) => {
      // Act
      const { el } = setup(issue(state));

      // Assert
      expect(el.querySelector('.issue-detail__triage-fact')).toBeNull();
    });
  });

  describe('run', () => {
    it('shows no run block without a run', () => {
      // Act
      const { el } = setup(mockDetail, null);

      // Assert
      expect(el.querySelector('.issue-detail__worker-run')).toBeNull();
    });

    it('shows a Run header with the run role tag, then the result', () => {
      // Act
      const { el } = setup(issue('failed'), runDetail({ role: 'triage', state: 'failed', resultText: 'Partial work done.' }));

      // Assert
      const block = el.querySelector('.issue-detail__worker-run');
      expect(block?.firstElementChild?.classList.contains('issue-detail__run-header')).toBe(true);
      expect(block?.querySelector('fd-role-tag')?.textContent).toBe('Role: triage');
      expect(text(block?.querySelector('.issue-detail__result-body'))).toBe('Partial work done.');
    });

    it('renders the published facts, then the reverted files, first in the run block', () => {
      // Act
      const { el } = setup(
        issue('review'),
        runDetail({
          publish: { headSha: 'abcdef1234567890', headCommitUrl: null, pullRequestUrl: null, branchCommitCount: 2 },
          revertedFiles: [{ path: 'assets/demo.mp4', reason: 'oversized', sizeBytes: 50_541_363 }],
        }),
      );

      // Assert
      const block = el.querySelector('.issue-detail__worker-run');
      expect(block?.firstElementChild?.tagName.toLowerCase()).toBe('fd-publish-facts');
      expect(block?.querySelector('fd-reverted-files')?.previousElementSibling?.tagName.toLowerCase()).toBe('fd-publish-facts');
    });

    it('renders the stored log with timestamped lines for a run with a stored log', () => {
      // Arrange
      const getLog = vi.fn(() => of<string | null>('2026-11-09T10:00:00Z PR opened\n2026-11-09T10:20:00Z exiting'));

      // Act
      const { el } = setup(issue('review'), runDetail({ workerRunId: 'run-done', hasStoredLog: true }), getLog);

      // Assert
      expect(getLog).toHaveBeenCalledWith('run-done');
      expect(Array.from(el.querySelectorAll('fd-log-view .log-view__line')).map((l) => l.textContent)).toEqual([
        '2026-11-09T10:00:00Z PR opened',
        '2026-11-09T10:20:00Z exiting',
      ]);
    });

    it('does not fetch the log again when the same run reloads unchanged', () => {
      // Arrange
      const getLog = vi.fn(() => of<string | null>('line'));
      const { fixture } = setup(issue('review'), runDetail({ hasStoredLog: true }), getLog);

      // Act
      fixture.componentRef.setInput('run', runDetail({ hasStoredLog: true }));
      fixture.detectChanges();

      // Assert
      expect(getLog).toHaveBeenCalledTimes(1);
    });

    it.each(['publishing', 'publish_failed', 'publish_blocked'] as const)(
      'shows no log view and fetches no log for a run in %s',
      (state) => {
        // Arrange
        const getLog = vi.fn(() => of<string | null>('line'));

        // Act
        const { el } = setup(issue('publish_failed'), runDetail({ state, hasStoredLog: true }), getLog);

        // Assert
        expect(el.querySelector('fd-log-view')).toBeNull();
        expect(getLog).not.toHaveBeenCalled();
      },
    );
  });
});
