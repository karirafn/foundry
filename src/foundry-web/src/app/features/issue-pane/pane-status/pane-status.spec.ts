import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { IssueDetail, IssueStateDetails, IssueSummary } from '../../issues/issue.model';
import { IssueService, TriageAction } from '../../issues/issue.service';
import { WorkerRunDetail } from '../../workers/worker-run.model';
import { WorkerRunService } from '../../workers/worker-run.service';
import { PaneStatusComponent } from './pane-status';

function stateDetails(overrides: Partial<IssueStateDetails> = {}): IssueStateDetails {
  return {
    workerRunId: 'run-1',
    branchName: 'feat/42-dark-mode',
    feedbackCutoffAt: null,
    failureReason: null,
    failedAt: null,
    completedAt: null,
    blockedBy: null,
    violations: null,
    transientRetry: null,
    ...overrides,
  };
}

function issue(state: IssueDetail['state'], details: Partial<IssueStateDetails> = {}): IssueDetail {
  return {
    id: 'abc',
    issueNumber: 42,
    title: 'Enable dark mode',
    state,
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/api/issues/42',
    stage: null,
    needsAction: false,
    awaitsDispatch: false,
    waitsOnBlockers: false,
    codeStep: null,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    author: 'dev',
    labels: [],
    stateDetails: stateDetails(details),
    kind: { source: 'labelled', kind: 'feature', conflictingTriageKind: null },
    triage: null,
  };
}

function summary(overrides: Partial<IssueSummary> = {}): IssueSummary {
  return {
    id: 'abc',
    issueNumber: 42,
    title: 'Enable dark mode',
    state: 'in_progress',
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/api/issues/42',
    providerType: 'github',
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

function run(overrides: Partial<WorkerRunDetail> = {}): WorkerRunDetail {
  return {
    workerRunId: 'run-1',
    issueId: 'abc',
    role: 'implement',
    state: 'running',
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

function setup(detail: IssueDetail | null, options: { run?: WorkerRunDetail | null; summary?: IssueSummary | null; error?: string | null } = {}) {
  const service = {
    retrying: signal(false),
    retryError: signal<string | null>(null),
    retrySuccess: signal<string | null>(null),
    triageActionPending: signal<TriageAction | null>(null),
    triageActionError: signal<string | null>(null),
    triageActionSuccess: signal<string | null>(null),
    retryIssue: vi.fn(),
    retryIssueFresh: vi.fn(),
    retryTriage: vi.fn(),
    skipTriage: vi.fn(),
    retriage: vi.fn(),
  };
  TestBed.configureTestingModule({
    imports: [PaneStatusComponent],
    providers: [
      { provide: IssueService, useValue: service },
      {
        provide: WorkerRunService,
        useValue: {
          requestCancel: vi.fn(() => of(null)),
          requestPublishRetry: vi.fn(() => of(null)),
          requestDiscard: vi.fn(() => of(null)),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(PaneStatusComponent);
  fixture.componentRef.setInput('detail', detail);
  fixture.componentRef.setInput('run', options.run ?? null);
  fixture.componentRef.setInput('summary', options.summary === undefined ? summary() : options.summary);
  fixture.componentRef.setInput('error', options.error ?? null);
  const retries: number[] = [];
  fixture.componentInstance.retry.subscribe(() => retries.push(1));
  document.body.appendChild(fixture.nativeElement);
  const render = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
  };
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const callouts = () => el.querySelectorAll('.pane-callout');
  const callout = () => el.querySelector('.pane-callout') as HTMLElement;
  const announcement = () => el.querySelector('[role="status"]')?.textContent?.trim() ?? '';
  return { fixture, el, service, retries, render, callouts, callout, announcement };
}

function text(node: Element | null | undefined): string {
  return (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

describe('PaneStatusComponent', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('shows three busy shimmer bars until the detail lands', () => {
    // Act
    const { el } = setup(null);

    // Assert
    const skeleton = el.querySelector('[aria-busy="true"]');
    expect(skeleton?.querySelectorAll('.pane-status__bar').length).toBe(3);
    expect(text(skeleton)).toBe('Loading issue details…');
    expect(el.querySelector('.pane-callout')).toBeNull();
  });

  it('shows a load failure as an alert with a Retry button that asks to reload', () => {
    // Arrange
    const { el, retries } = setup(null, { error: 'Failed to load issue details' });

    // Act
    (el.querySelector('[role="alert"] button') as HTMLButtonElement).click();

    // Assert
    expect(text(el.querySelector('[role="alert"] span'))).toBe('Failed to load issue details');
    expect(retries.length).toBe(1);
  });

  it('leads with the stage, the state badge and the step chip', () => {
    // Act
    const { el } = setup(issue('checks_pending'), { summary: summary({ state: 'checks_pending', codeStep: 'checks' }) });

    // Assert
    const line = el.querySelector('.pane-status__line');
    expect(text(line?.querySelector('.pane-status__stage'))).toBe('Implement');
    expect(line?.querySelector('fd-state-badge')).not.toBeNull();
    expect(text(line?.querySelector('fd-step-chip'))).toBe('Checks running…');
  });

  it.each([
    ['triage', 'Triage'],
    ['awaiting_review', 'Your review'],
    ['done', 'Done'],
  ] as const)('labels the %s stage "%s"', (stage, label) => {
    // Act
    const { el } = setup(issue('review'), { summary: summary({ stage }) });

    // Assert
    expect(text(el.querySelector('.pane-status__stage'))).toBe(label);
  });

  it('shows exactly one callout, its title a focusable heading, in its tone', () => {
    // Act
    const { callouts, callout } = setup(issue('failed', { failureReason: 'Exit code 1' }));

    // Assert
    expect(callouts().length).toBe(1);
    expect(callout().classList.contains('pane-callout--error')).toBe(true);
    const title = callout().querySelector('h3.pane-callout__title');
    expect(text(title)).toBe('Run failed');
    expect(title?.getAttribute('tabindex')).toBe('-1');
    expect(text(callout())).toContain('Exit code 1');
    expect(text(callout().querySelector('fd-pane-actions button'))).toBe('Retry issue');
  });

  it('gives the visible callout no live role, since it holds buttons', () => {
    // Act
    const { callout } = setup(issue('failed'));

    // Assert
    expect(callout().getAttribute('role')).toBeNull();
    expect(callout().getAttribute('aria-live')).toBeNull();
  });

  it('shows the failure chip as an image named by its category', () => {
    // Act
    const { callout } = setup(issue('failed'), { run: run({ state: 'failed', failureCategory: 'timed_out' }) });

    // Assert
    const chip = callout().querySelector('.badge');
    expect(chip?.classList.contains('badge--failure-timed-out')).toBe(true);
    expect(chip?.getAttribute('role')).toBe('img');
    expect(chip?.getAttribute('aria-label')).toBe('Failure category: TIMED OUT');
  });

  it('labels the callout list by its visible heading and links each failing check', () => {
    // Act
    const { el, callout } = setup(
      issue('failed', {
        checksOutcome: 'still_failing',
        failingChecks: [{ name: 'build', conclusion: 'failure', detailsUrl: 'https://ci.example/1' }],
      }),
    );

    // Assert
    const list = callout().querySelector('ul');
    const headingId = list?.getAttribute('aria-labelledby');
    expect(text(el.querySelector(`[id="${headingId}"]`))).toBe('Failing checks');
    const link = list?.querySelector('a');
    expect(link?.getAttribute('aria-label')).toBe('View details for failing check build');
    expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
    expect(list?.querySelector('[role="img"]')?.getAttribute('aria-label')).toBe('Conclusion: failure');
  });

  it('renders the secret findings with their heading for a blocked push', () => {
    // Act
    const { callout } = setup(issue('publish_blocked'), {
      run: run({ state: 'publish_blocked', secretFindings: [{ path: 'a.env', line: 3, commitSha: 'abc', ruleId: 'aws-key' }] }),
    });

    // Assert
    const findings = callout().querySelector('fd-secret-findings');
    expect(findings?.querySelectorAll('li').length).toBe(1);
    const list = findings?.querySelector('ul');
    expect(callout().querySelector(`[id="${list?.getAttribute('aria-labelledby')}"]`)).not.toBeNull();
    expect(text(callout().querySelector('fd-pane-actions .pane-actions'))).toBe('Discard run');
  });

  it('renders link rows for a diverged branch', () => {
    // Act
    const { callout } = setup(
      issue('branch_diverged', {
        branchUrl: 'https://github.com/acme/api/tree/feat/42-dark-mode',
        sideBranchName: 'feat/42-run',
        sideBranchUrl: 'https://github.com/acme/api/tree/feat/42-run',
      }),
    );

    // Assert
    const rows = callout().querySelector('dl.pane-callout__rows');
    expect(Array.from(rows?.querySelectorAll('dt') ?? []).map(text)).toEqual(['Issue branch', "Run's work"]);
    expect(Array.from(rows?.querySelectorAll('dd a') ?? []).map((a) => a.getAttribute('aria-label'))).toEqual([
      'View branch feat/42-dark-mode on GitHub',
      "View the run's branch feat/42-run on GitHub",
    ]);
  });

  it('links the pull request from a review callout in a new tab', () => {
    // Act
    const { callout } = setup(issue('review'), {
      summary: summary({ state: 'review', pullRequestUrl: 'https://github.com/acme/api/pull/7' }),
    });

    // Assert
    const link = callout().querySelector('.pane-callout__links a');
    expect(text(link)).toBe('View PR');
    expect(link?.getAttribute('href')).toBe('https://github.com/acme/api/pull/7');
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
    expect(link?.getAttribute('aria-label')).toBe('View pull request for issue #42');
  });

  it('turns a completed issue into the Completed callout with no actions, keeping the pane content', async () => {
    // Arrange
    const { fixture, render, callout } = setup(issue('in_progress'), { run: run() });

    // Act
    fixture.componentRef.setInput('detail', issue('completed'));
    fixture.componentRef.setInput('run', run({ state: 'completed' }));
    await render();

    // Assert
    expect(text(callout().querySelector('.pane-callout__title'))).toBe('Completed');
    expect(callout().classList.contains('pane-callout--success')).toBe(true);
    expect(callout().querySelector('fd-pane-actions button')).toBeNull();
  });

  describe('announcements', () => {
    it('keeps one status region mounted, empty on the first callout', () => {
      // Act
      const { el, announcement } = setup(issue('in_progress'));

      // Assert
      expect(el.querySelectorAll('[role="status"]').length).toBe(1);
      expect(announcement()).toBe('');
    });

    it('announces the callout title when it changes', async () => {
      // Arrange
      const { fixture, render, announcement } = setup(issue('in_progress'));

      // Act
      fixture.componentRef.setInput('detail', issue('review'));
      await render();

      // Assert
      expect(announcement()).toBe('Status: PR ready for review');
    });

    it('does not announce again when the detail reloads with the same title', async () => {
      // Arrange
      const { fixture, render, announcement } = setup(issue('in_progress'));
      fixture.componentRef.setInput('detail', issue('review'));
      await render();
      const first = announcement();

      // Act
      fixture.componentRef.setInput('detail', issue('review', { branchName: 'feat/other' }));
      await render();

      // Assert
      expect(first).toBe('Status: PR ready for review');
      expect(announcement()).toBe('Status: PR ready for review');
    });
  });

  describe('focus after an action', () => {
    it('moves focus to the callout title once the reloaded detail lands', async () => {
      // Arrange
      const { fixture, el, service, render } = setup(issue('failed'));
      (el.querySelector('fd-pane-actions button') as HTMLButtonElement).click();
      service.retrySuccess.set('Retry queued. Issue status is updating.');
      await render();
      const focusedBeforeReload = document.activeElement;

      // Act
      fixture.componentRef.setInput('detail', issue('queued'));
      await render();

      // Assert
      expect(focusedBeforeReload?.classList.contains('pane-callout__title')).toBe(false);
      expect(document.activeElement?.classList.contains('pane-callout__title')).toBe(true);
      expect(text(document.activeElement)).toBe('Queued');
    });

    it('does not move focus on an ordinary detail reload', async () => {
      // Arrange
      const { fixture, render } = setup(issue('failed'));

      // Act
      fixture.componentRef.setInput('detail', issue('queued'));
      await render();

      // Assert
      expect(document.activeElement?.classList.contains('pane-callout__title')).toBe(false);
    });
  });
});
