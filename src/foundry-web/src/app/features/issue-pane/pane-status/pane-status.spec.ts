import { WritableSignal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { AgentActivity, AgentTaskItem } from '../../../core/models/agent-activity.model';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { IssueDetail, IssueStateDetails, IssueSummary } from '../../issues/issue.model';
import { LifecycleStep } from '../../issues/lifecycle.model';
import { IssueService } from '../../issues/issue.service';
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
    runKind: null,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    round: 1,
    author: 'dev',
    labels: [],
    stateDetails: stateDetails(details),
    kind: { source: 'labelled', kind: 'feature', conflictingTriageKind: null },
    triage: null,
    clarifications: [],
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
    runKind: null,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    round: 1,
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
    publishBlockedAt: null,
    falsePositivesConfirmedAt: null,
    confirmedFindings: [],
    toolchain: null,
    ...overrides,
  };
}

const IMPLEMENTING: readonly LifecycleStep[] = [
  { step: 'triage', actor: 'claude', status: 'done', phase: null },
  { step: 'implement', actor: 'claude', status: 'current', phase: 'running' },
  { step: 'publish', actor: 'code', status: 'upcoming', phase: null },
  { step: 'your_review', actor: 'person', status: 'upcoming', phase: null },
];

const PUBLISHING: readonly LifecycleStep[] = [
  IMPLEMENTING[0],
  { step: 'implement', actor: 'claude', status: 'done', phase: null },
  { step: 'publish', actor: 'code', status: 'current', phase: 'running' },
  IMPLEMENTING[3],
];

const TASKS: readonly AgentTaskItem[] = [
  { id: '1', subject: 'Read the criteria', activeForm: null, status: 'completed' },
  { id: '2', subject: 'Write failing tests', activeForm: 'Writing failing tests', status: 'in_progress' },
  { id: '3', subject: 'Run the suite', activeForm: null, status: 'pending' },
];

function agentActivity(tasks: readonly AgentTaskItem[] = TASKS): AgentActivity {
  return {
    workerRunId: 'run-1',
    issueId: 'abc',
    role: 'implement',
    lastEventAt: null,
    tasks,
    currentTask: tasks.find((t) => t.status === 'in_progress') ?? null,
    completedTaskCount: tasks.filter((t) => t.status === 'completed').length,
    subagents: [],
    inFlightTool: null,
    tokens: { inputTokens: 0, outputTokens: null },
    backgroundTasks: [],
  };
}

function setup(
  detail: IssueDetail | null,
  options: {
    run?: WorkerRunDetail | null;
    summary?: IssueSummary | null;
    error?: string | null;
    activity?: AgentActivity | null;
  } = {},
) {
  const activity: WritableSignal<AgentActivity | null> = signal(options.activity ?? null);
  const service = {
    retryIssue: vi.fn(() => of<string | null>(null)),
    retryIssueFresh: vi.fn(() => of<string | null>(null)),
    retryTriage: vi.fn(() => of<string | null>(null)),
    skipTriage: vi.fn(() => of<string | null>(null)),
    retriage: vi.fn(() => of<string | null>(null)),
  };
  TestBed.configureTestingModule({
    imports: [PaneStatusComponent],
    providers: [
      { provide: IssueService, useValue: service },
      {
        provide: WorkerSignalRService,
        useValue: {
          agentActivityForIssue: (id: string) => (activity()?.issueId === id ? activity() : null),
        },
      },
      {
        provide: WorkerRunService,
        useValue: {
          requestCancel: vi.fn(() => of(null)),
          requestPublishRetry: vi.fn(() => of(null)),
          requestDiscard: vi.fn(() => of(null)),
          requestConfirmFalsePositives: vi.fn(() => of(null)),
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
  return { fixture, el, service, retries, render, callouts, callout, announcement, activity };
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

  it('shows a still waiting chip and the catch-up badge while waiting to catch up', () => {
    // Act
    const { el } = setup(issue('merge_pending'), {
      summary: summary({ state: 'merge_pending', codeStep: 'catch_up_waiting' }),
    });

    // Assert
    const line = el.querySelector('.pane-status__line');
    expect(text(line?.querySelector('fd-step-chip'))).toBe('Waiting to catch up');
    expect(line?.querySelector('.step-chip--waiting')).not.toBeNull();
    expect(text(line?.querySelector('fd-state-badge span'))).toBe('CATCH-UP QUEUED');
  });

  it('shows MERGING when opened without a board summary', () => {
    // Act
    const { el } = setup(issue('merge_pending'));

    // Assert
    expect(text(el.querySelector('.pane-status__line fd-state-badge span'))).toBe('MERGING');
  });

  it.each([
    ['triage', 'Triage'],
    ['merge', 'Merge'],
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

  it('renders the secret findings with their heading for a blocked push, offering Discard run then the confirmation', () => {
    // Act
    const { callout } = setup(issue('publish_blocked'), {
      run: run({ state: 'publish_blocked', secretFindings: [{ path: 'a.env', line: 3, commitSha: 'abc', ruleId: 'aws-key' }] }),
    });

    // Assert
    const findings = callout().querySelector('fd-secret-findings');
    expect(findings?.querySelectorAll('li').length).toBe(1);
    const list = findings?.querySelector('ul');
    expect(callout().querySelector(`[id="${list?.getAttribute('aria-labelledby')}"]`)).not.toBeNull();
    const buttons = Array.from(callout().querySelectorAll('fd-pane-actions .pane-actions button')).map((b) => text(b));
    expect(buttons).toEqual(['Discard run', 'Confirm all as false positives and publish']);
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
      const { fixture, el, render } = setup(issue('failed'));
      (el.querySelector('fd-pane-actions button') as HTMLButtonElement).click();
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

    it('moves focus to the Publishing callout title once findings are confirmed and the reload lands', async () => {
      // Arrange
      const blocked = run({
        state: 'publish_blocked',
        secretFindings: [{ path: 'a.env', line: 3, commitSha: 'abc', ruleId: 'aws-key' }],
        publishBlockedAt: '2026-11-14T09:00:00+00:00',
      });
      const { fixture, el, render } = setup(issue('publish_blocked'), { run: blocked });
      const confirmAll = Array.from(el.querySelectorAll<HTMLButtonElement>('fd-pane-actions .pane-actions button'))
        .find((b) => text(b) === 'Confirm all as false positives and publish')!;
      confirmAll.click();
      await render();
      const dialog = Array.from(el.querySelectorAll<HTMLDialogElement>('dialog')).find((d) => d.open)!;
      (dialog.querySelector('input[type="checkbox"]') as HTMLInputElement).click();
      await render();
      (dialog.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
      await render();

      // Act
      fixture.componentRef.setInput('detail', issue('in_progress'));
      fixture.componentRef.setInput('run', run({ state: 'publishing', falsePositivesConfirmedAt: '2026-11-14T09:05:00+00:00' }));
      await render();

      // Assert
      expect(el.querySelector('.pane-actions__success--confirm-false-positives')?.textContent?.trim()).toBe(
        'Findings confirmed. Publishing restarted; issue status is updating.',
      );
      expect(document.activeElement?.classList.contains('pane-callout__title')).toBe(true);
      expect(text(document.activeElement)).toBe('Publishing');
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

  describe('lifecycle', () => {
    const enteredTwelveMinutesAgo = () => new Date(Date.now() - 12 * 60_000 - 5_000).toISOString();

    function implementing(overrides: Partial<IssueSummary> = {}): IssueSummary {
      return summary({ lifecycle: IMPLEMENTING, enteredStateAt: enteredTwelveMinutesAgo(), ...overrides });
    }

    afterEach(() => {
      vi.useRealTimers();
    });

    it('shows the vertical rail between the status line and the callout', () => {
      // Act
      const { el } = setup(issue('in_progress'), { summary: implementing() });

      // Assert
      const section = el.querySelector('fd-pane-lifecycle section')!;
      expect(text(section.querySelector('h3'))).toBe('Lifecycle');
      const order = Array.from(el.querySelectorAll('.pane-status__line, fd-pane-lifecycle, .pane-callout'));
      expect(order.map((n) => n.localName === 'fd-pane-lifecycle' ? 'rail' : n.className.split(' ')[0])).toEqual([
        'pane-status__line',
        'rail',
        'pane-callout',
      ]);
      expect(section.querySelector('fd-lifecycle-rail ol[aria-label="Lifecycle"]')).not.toBeNull();
    });

    it("shows the current step's status with the time since the issue entered it", () => {
      // Act
      const { el } = setup(issue('in_progress'), { summary: implementing() });

      // Assert
      expect(text(el.querySelector('li[aria-current="step"] .rail__status'))).toBe('Running · 12m');
    });

    it('ticks the elapsed time while a step is current', async () => {
      // Arrange
      vi.useFakeTimers({ now: Date.parse('2026-11-24T10:12:05Z'), toFake: ['Date', 'setInterval', 'clearInterval'] });
      const { render, el } = setup(issue('in_progress'), {
        summary: implementing({ enteredStateAt: '2026-11-24T10:00:00Z' }),
      });

      // Act
      vi.advanceTimersByTime(60_000);
      await render();

      // Assert
      expect(text(el.querySelector('li[aria-current="step"] .rail__status'))).toBe('Running · 13m');
    });

    it('shows a Round pill past the first round', () => {
      // Act
      const { el } = setup(issue('in_progress'), { summary: implementing({ round: 2 }) });

      // Assert
      expect(text(el.querySelector('.pane-lifecycle__round'))).toBe('Round 2');
    });

    it('shows no Round pill in the first round', () => {
      // Act
      const { el } = setup(issue('in_progress'), { summary: implementing() });

      // Assert
      expect(el.querySelector('.pane-lifecycle__round')).toBeNull();
    });

    it.each([
      ['no summary', null],
      ['an empty lifecycle', summary({ lifecycle: [] })],
      ['no lifecycle', summary()],
    ])('shows no rail with %s', (_, value) => {
      // Act
      const { el } = setup(issue('in_progress'), { summary: value });

      // Assert
      expect(el.querySelector('fd-pane-lifecycle')).toBeNull();
      expect(el.querySelector('.pane-callout')).not.toBeNull();
    });

    it('shows no rail when the detail failed to load', () => {
      // Act
      const { el } = setup(null, { summary: implementing(), error: 'Failed to load issue details' });

      // Assert
      expect(el.querySelector('fd-pane-lifecycle')).toBeNull();
    });

    it("nests the agent's tasks under the running Claude step, expanded behind a toggle", () => {
      // Act
      const { el } = setup(issue('in_progress'), { summary: implementing(), activity: agentActivity() });

      // Assert
      const current = el.querySelector('li[aria-current="step"]')!;
      const toggle = current.querySelector<HTMLButtonElement>('button.pane-lifecycle__toggle')!;
      expect(text(toggle)).toBe('Tasks, task 2 of 3');
      expect(toggle.getAttribute('aria-expanded')).toBe('true');
      const list = el.querySelector(`[id="${toggle.getAttribute('aria-controls')}"]`) as HTMLElement;
      expect(current.contains(list)).toBe(true);
      expect(list.closest('[hidden]')).toBeNull();
      expect(Array.from(list.querySelectorAll('li')).map((li) => li.getAttribute('data-state'))).toEqual([
        'done',
        'current',
        'upcoming',
      ]);
    });

    it('collapses the task list when the toggle is pressed, keeping focus on it', async () => {
      // Arrange
      const { el, render } = setup(issue('in_progress'), { summary: implementing(), activity: agentActivity() });
      const toggle = el.querySelector<HTMLButtonElement>('button.pane-lifecycle__toggle')!;
      toggle.focus();

      // Act
      toggle.click();
      await render();

      // Assert
      expect(toggle.getAttribute('aria-expanded')).toBe('false');
      expect(el.querySelector('fd-task-list')?.hasAttribute('hidden')).toBe(true);
      expect(document.activeElement).toBe(toggle);
    });

    it('shows no tasks once the Claude step is done', () => {
      // Act
      const { el } = setup(issue('in_progress'), {
        summary: implementing({ lifecycle: PUBLISHING }),
        activity: agentActivity(),
      });

      // Assert
      expect(el.querySelector('.pane-lifecycle__toggle, fd-task-list')).toBeNull();
    });

    it('shows no toggle for a run with no task list', () => {
      // Act
      const { el } = setup(issue('in_progress'), { summary: implementing(), activity: agentActivity([]) });

      // Assert
      expect(el.querySelector('.pane-lifecycle__toggle')).toBeNull();
    });

    it('moves focus to the callout title when the step ends while the toggle has focus', async () => {
      // Arrange
      const { fixture, el, render } = setup(issue('in_progress'), {
        summary: implementing(),
        activity: agentActivity(),
      });
      el.querySelector<HTMLButtonElement>('button.pane-lifecycle__toggle')!.focus();

      // Act
      fixture.componentRef.setInput('summary', implementing({ lifecycle: PUBLISHING }));
      await render();
      await render();

      // Assert
      expect(el.querySelector('.pane-lifecycle__toggle')).toBeNull();
      expect(document.activeElement?.classList.contains('pane-callout__title')).toBe(true);
    });

    it('announces nothing about the lifecycle on the first render', () => {
      // Act
      const { announcement } = setup(issue('in_progress'), { summary: implementing() });

      // Assert
      expect(announcement()).toBe('');
    });

    it('announces the new step when the current step changes', async () => {
      // Arrange
      const { fixture, render, announcement } = setup(issue('in_progress'), { summary: implementing() });

      // Act
      fixture.componentRef.setInput('summary', implementing({ lifecycle: PUBLISHING }));
      await render();

      // Assert
      expect(announcement()).toBe('Step: Publish');
    });

    it('announces the round when it changes', async () => {
      // Arrange
      const { fixture, render, announcement } = setup(issue('in_progress'), { summary: implementing() });

      // Act
      fixture.componentRef.setInput('summary', implementing({ round: 2 }));
      await render();

      // Assert
      expect(announcement()).toBe('Round 2');
    });

    it('announces the callout, step and round together when all change at once', async () => {
      // Arrange
      const { fixture, render, announcement } = setup(issue('in_progress'), { summary: implementing() });

      // Act
      fixture.componentRef.setInput('detail', issue('review'));
      fixture.componentRef.setInput('summary', implementing({ lifecycle: PUBLISHING, round: 2 }));
      await render();

      // Assert
      expect(announcement()).toBe('Status: PR ready for review. Step: Publish. Round 2');
    });
  });
});
