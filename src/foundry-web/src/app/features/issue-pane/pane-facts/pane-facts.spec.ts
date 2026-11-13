import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { IssueDetail, IssueStateDetails } from '../../issues/issue.model';
import { WorkerRunDetail } from '../../workers/worker-run.model';
import { WorkerRunService } from '../../workers/worker-run.service';
import { PaneFactsComponent } from './pane-facts';

const stateDetails: IssueStateDetails = {
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

const baseDetail: IssueDetail = {
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
  stateDetails,
  kind: { source: 'labelled', kind: 'feature', conflictingTriageKind: null },
  triage: null,
};

function issue(state: string, overrides: Partial<IssueDetail> = {}): IssueDetail {
  return { ...baseDetail, state: state as IssueDetail['state'], ...overrides };
}

function runDetail(overrides: Partial<WorkerRunDetail> = {}): WorkerRunDetail {
  return {
    workerRunId: 'run-0123456789',
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

function setup(detail: IssueDetail | null, run: WorkerRunDetail | null = null, getLog = vi.fn(() => of<string | null>(null))) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [PaneFactsComponent],
    providers: [{ provide: WorkerRunService, useValue: { getLog } }],
  });
  const fixture = TestBed.createComponent(PaneFactsComponent);
  fixture.componentRef.setInput('detail', detail);
  fixture.componentRef.setInput('run', run);
  fixture.componentRef.setInput('summary', null);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return { fixture, el, getLog };
}

function text(node: Element | null | undefined): string {
  return (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

/** The dd that follows the dt with the given text. */
function value(el: HTMLElement, key: string): HTMLElement | null {
  const dt = Array.from(el.querySelectorAll('dt')).find((d) => text(d) === key);
  return (dt?.nextElementSibling as HTMLElement | null) ?? null;
}

describe('PaneFactsComponent', () => {
  it('heads the facts with a Details heading that names the section', () => {
    // Act
    const { el } = setup(baseDetail);

    // Assert
    const section = el.querySelector('section')!;
    const heading = el.querySelector(`#${section.getAttribute('aria-labelledby')}`);
    expect(heading?.tagName.toLowerCase()).toBe('h3');
    expect(text(heading)).toBe('Details');
  });

  it('renders each fact as a dt and its dd', () => {
    // Act
    const { el } = setup(baseDetail);

    // Assert
    expect(Array.from(el.querySelectorAll('dl > dt')).map(text)).toEqual(['Kind', 'Branch', 'Labels', 'Author', 'Completed']);
    expect(text(value(el, 'Author'))).toBe('dev');
    expect(text(value(el, 'Branch'))).toBe('feat/dark-mode');
    expect(value(el, 'Branch')?.classList.contains('pane-facts__mono')).toBe(true);
  });

  it('shows three shimmer bars marked busy while the detail loads', () => {
    // Act
    const { el } = setup(null);

    // Assert
    const skeleton = el.querySelector('[aria-busy="true"]');
    expect(skeleton?.querySelectorAll('.pane-facts__bar').length).toBe(3);
    expect(el.querySelector('dl')).toBeNull();
  });

  it('shows the kind with its source note and the Triage differs chip and sentence', () => {
    // Act
    const { el } = setup(issue('queued', { kind: { source: 'labelled', kind: 'bug', conflictingTriageKind: 'refactor' } }));

    // Assert
    const kind = value(el, 'Kind');
    expect(text(kind?.querySelector('.pane-facts__kind-value'))).toBe('Bug');
    expect(text(kind?.querySelector('.pane-facts__note'))).toBe('From label');
    expect(text(kind?.querySelector('.pane-facts__chip--warning'))).toBe('Triage differs');
    expect(text(kind)).toContain('Triage read it as a refactor. The label decides; nothing changes.');
  });

  it('shows a triage verdict as a toned chip with its kind and decision date', () => {
    // Act
    const { el } = setup(
      issue('queued', { triage: { outcome: 'ready', kind: 'feature', questions: [], decidedAt: '2026-11-06T14:14:03Z' } }),
    );

    // Assert
    const triage = value(el, 'Triage');
    expect(text(triage?.querySelector('.pane-facts__chip--success'))).toBe('Ready');
    expect(text(triage)).toMatch(/Feature/);
    expect(triage?.querySelector('time')?.getAttribute('datetime')).toBe('2026-11-06T14:14:03Z');
    expect(text(triage)).toMatch(/Triaged Nov 6, 2026/);
  });

  it('says a queued issue without a verdict was not triaged', () => {
    // Act
    const { el } = setup(issue('queued', { stateDetails: null }));

    // Assert
    expect(text(value(el, 'Triage'))).toBe('Not triaged. This issue was queued without a readiness check.');
  });

  it('links the short head SHA to the commit, with the full SHA as title and name, beside the commit count', () => {
    // Act
    const { el } = setup(
      issue('review'),
      runDetail({ publish: { headSha: '0123456789abcdef', headCommitUrl: 'https://github.com/o/r/commit/0123456', pullRequestUrl: null, branchCommitCount: 2 } }),
    );

    // Assert
    const published = value(el, 'Published');
    const sha = published?.querySelector('.pane-facts__sha') as HTMLElement;
    expect(sha.tagName.toLowerCase()).toBe('a');
    expect(text(sha)).toBe('0123456');
    expect(sha.getAttribute('href')).toBe('https://github.com/o/r/commit/0123456');
    expect(sha.getAttribute('title')).toBe('0123456789abcdef');
    expect(sha.getAttribute('aria-label')).toBe('Head commit 0123456789abcdef');
    expect(text(published)).toContain('2 commits');
  });

  it('shows the head SHA as text when its link is not https', () => {
    // Act
    const { el } = setup(
      issue('review'),
      runDetail({ publish: { headSha: '0123456789abcdef', headCommitUrl: 'javascript:alert(1)', pullRequestUrl: null, branchCommitCount: 2 } }),
    );

    // Assert
    expect(value(el, 'Published')?.querySelector('a')).toBeNull();
    expect(text(value(el, 'Published')?.querySelector('.pane-facts__sha'))).toBe('0123456');
  });

  it('renders the reverted files in a full-width row', () => {
    // Act
    const { el } = setup(issue('review'), runDetail({ revertedFiles: [{ path: 'assets/demo.mp4', reason: 'oversized', sizeBytes: 50_541_363 }] }));

    // Assert
    const dd = el.querySelector('dd fd-reverted-files')?.closest('dd');
    expect(dd?.classList.contains('pane-facts__full')).toBe(true);
    expect(text(dd)).toContain('assets/demo.mp4');
  });

  it('shows the run role tag and the 8-character run ID with the full ID as title', () => {
    // Act
    const { el } = setup(issue('failed'), runDetail({ role: 'triage' }));

    // Assert
    const run = value(el, 'Run');
    expect(run?.querySelector('fd-role-tag')?.textContent).toBe('Role: triage');
    const id = run?.querySelector('.pane-facts__mono');
    expect(text(id)).toBe('run-0123');
    expect(id?.getAttribute('title')).toBe('run-0123456789');
  });

  it('lists each blocker in a list named by its key', () => {
    // Act
    const { el } = setup(
      issue('blocked', { stateDetails: { ...stateDetails, blockedBy: [{ issueNumber: 12, reason: null }, { issueNumber: 14, reason: 'waits on the schema' }] } }),
    );

    // Assert
    const list = value(el, 'Blocked by')?.querySelector('ul');
    expect(Array.from(list?.querySelectorAll('li') ?? []).map(text)).toEqual(['#12', '#14 Triage: waits on the schema']);
    expect(text(el.querySelector(`[id="${list?.getAttribute('aria-labelledby')}"]`))).toBe('Blocked by');
  });

  it('renders each label as a pill', () => {
    // Act
    const { el } = setup(baseDetail);

    // Assert
    expect(Array.from(value(el, 'Labels')?.querySelectorAll('.pane-facts__pill') ?? []).map(text)).toEqual(['enhancement', 'ui']);
  });

  it('shows dates in a time element with a machine-readable datetime', () => {
    // Act
    const { el } = setup(baseDetail);

    // Assert
    const time = value(el, 'Completed')?.querySelector('time');
    expect(time?.getAttribute('datetime')).toBe('2026-02-01T12:00:00Z');
    expect(text(time)).toMatch(/Feb 1, 2026/);
  });

  describe('run result', () => {
    it('shows a short result without a toggle', () => {
      // Act
      const { el } = setup(issue('failed'), runDetail({ resultText: 'Partial work done.' }));

      // Assert
      const result = value(el, 'Run result');
      expect(text(result)).toBe('Partial work done.');
      expect(result?.classList.contains('pane-facts__full')).toBe(true);
      expect(result?.querySelector('button')).toBeNull();
    });

    it('clamps a long result behind Show more, which expands it in place', () => {
      // Arrange
      const { fixture, el } = setup(issue('failed'), runDetail({ resultText: 'a\nb\nc\nd\ne\nf' }));
      const result = value(el, 'Run result')!;
      const toggle = result.querySelector('button')!;
      expect(result.querySelector('.pane-facts__result--clamped')).not.toBeNull();
      expect(text(toggle)).toBe('Show more');
      expect(toggle.getAttribute('aria-expanded')).toBe('false');

      // Act
      toggle.click();
      fixture.detectChanges();

      // Assert
      expect(result.querySelector('.pane-facts__result--clamped')).toBeNull();
      expect(text(toggle)).toBe('Show less');
      expect(toggle.getAttribute('aria-expanded')).toBe('true');
      expect(toggle.getAttribute('aria-controls')).toBe(result.querySelector('.pane-facts__result')?.id);
    });
  });

  describe('log', () => {
    it('shows the stored log of a finished run collapsed behind Show log, bounded in height', () => {
      // Arrange
      const getLog = vi.fn(() => of<string | null>('2026-11-09T10:00:00Z PR opened\n2026-11-09T10:20:00Z exiting'));

      // Act
      const { el } = setup(issue('review'), runDetail({ workerRunId: 'run-done', hasStoredLog: true }), getLog);

      // Assert
      expect(getLog).toHaveBeenCalledWith('run-done');
      const log = el.querySelector('fd-log-view') as HTMLElement;
      expect(log.closest('dd')?.classList.contains('pane-facts__full')).toBe(true);
      expect(log.style.getPropertyValue('--fd-log-view-max-height')).toBe('16rem');
      expect(text(log.querySelector('.log-view__toggle-label'))).toBe('Show log');
      expect(log.querySelector('.log-view__toggle')?.getAttribute('aria-expanded')).toBe('false');
      expect(Array.from(log.querySelectorAll('.log-view__line')).map((l) => l.textContent)).toEqual([
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

    it('fetches no log for a run without a stored log', () => {
      // Act
      const { el, getLog } = setup(issue('in_progress'), runDetail({ state: 'running' }));

      // Assert
      expect(getLog).not.toHaveBeenCalled();
      expect(el.querySelector('fd-log-view')).toBeNull();
    });
  });
});
