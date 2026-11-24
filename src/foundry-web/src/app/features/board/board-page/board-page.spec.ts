import { ComponentFixture, TestBed } from '@angular/core/testing';
import { WritableSignal, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { Location } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { vi } from 'vitest';
import { LoadOutcome } from '../../../core/models/load-outcome';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { SettingsService } from '../../../core/services/settings.service';
import {
  FakeConnectionGate,
  provideFakeConnectionGate,
} from '../../../core/testing/fake-connection-gate';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { AgentActivity } from '../../../core/models/agent-activity.model';
import { DispatchService } from '../../../core/services/dispatch.service';
import { DispatchCapacityService } from '../../../core/services/dispatch-capacity.service';
import { AccountService } from '../../settings/accounts/account.service';
import { RepositorySummary } from '../../settings/repositories/repository.model';
import { RepositoryService } from '../../settings/repositories/repository.service';
import { IssueService } from '../../issues/issue.service';
import { BoardPageComponent } from './board-page';

function item(id: string, issueNumber: number, overrides: Record<string, unknown> = {}) {
  return {
    id,
    issueNumber,
    title: `Issue ${issueNumber}`,
    state: 'in_progress',
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: `https://github.com/acme/api/issues/${issueNumber}`,
    failureClassification: null,
    repositoryEligibilityStatus: 'eligible',
    runStats: null,
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: false,
    runKind: 'fresh',
    waitsOnBlockers: false,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

const hubHandlers = new Map<string, (payload: unknown) => void>();

interface Harness {
  el: HTMLElement;
  httpMock: HttpTestingController;
  gate: FakeConnectionGate;
  loadSettings: ReturnType<typeof vi.fn>;
  repositories: WritableSignal<RepositorySummary[]>;
  capacity: WritableSignal<{
    maxConcurrent: number;
    occupiedSlots: number;
    freeSlots: number;
  } | null>;
  capacityError: WritableSignal<string | null>;
  activities: WritableSignal<ReadonlyMap<string, AgentActivity>>;
  render: () => Promise<void>;
  fixture: ComponentFixture<BoardPageComponent>;
}

async function setup(url = '/'): Promise<Harness> {
  const gate = new FakeConnectionGate();
  const loadSettings = vi.fn(() => Promise.resolve<LoadOutcome>('loaded'));
  const repositories = signal<RepositorySummary[]>([]);
  const capacity = signal<{
    maxConcurrent: number;
    occupiedSlots: number;
    freeSlots: number;
  } | null>(null);
  const capacityError = signal<string | null>(null);
  const activities = signal<ReadonlyMap<string, AgentActivity>>(new Map());
  TestBed.configureTestingModule({
    imports: [BoardPageComponent],
    providers: [
      provideRouter([]),
      provideLocationMocks(),
      provideHttpClient(),
      provideHttpClientTesting(),
      provideFakeConnectionGate(gate),
      { provide: SettingsService, useValue: { loadSettings } },
      {
        provide: IssueSignalRService,
        useValue: {
          on: (name: string, handler: (payload: unknown) => void) => hubHandlers.set(name, handler),
          off: () => {},
          onReconnected: () => {},
        },
      },
      {
        provide: WorkerSignalRService,
        useValue: {
          activeAgentCount: signal(0),
          activityForIssue: () => null,
          agentActivityForIssue: (id: string) => activities().get(id) ?? null,
        },
      },
      {
        provide: DispatchCapacityService,
        useValue: { capacity, loadError: capacityError, load: () => Promise.resolve('loaded') },
      },
      {
        provide: AccountService,
        useValue: { loadAccounts: () => Promise.resolve('loaded'), accounts: signal([]) },
      },
      {
        provide: RepositoryService,
        useValue: { repositories, loadAllRepositories: () => Promise.resolve('loaded') },
      },
      {
        provide: DispatchService,
        useValue: {
          isDispatchPaused: signal(false),
          usageLimitResetsAt: signal(null),
          pausing: signal(false),
          resuming: signal(false),
          pauseResumeError: signal(null),
        },
      },
    ],
  });
  // Bootstrap sets the listener up in the app; Back and Forward reach the router through it.
  TestBed.inject(Router).setUpLocationChangeListener();
  await TestBed.inject(Router).navigateByUrl(url);
  const fixture = TestBed.createComponent(BoardPageComponent);
  document.body.appendChild(fixture.nativeElement);
  // The router defers a Back navigation to a macrotask, so a render waits one out.
  const render = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    await fixture.whenStable();
  };
  await render();
  return {
    el: fixture.nativeElement as HTMLElement,
    httpMock: TestBed.inject(HttpTestingController),
    gate,
    loadSettings,
    repositories,
    capacity,
    capacityError,
    activities,
    render,
    fixture,
  };
}

async function loaded(items: unknown[], url = '/'): Promise<Harness> {
  const h = await setup(url);
  h.httpMock.expectOne('/api/issues').flush({ items, nextCursor: null });
  await h.render();
  return h;
}

function text(el: Element | null | undefined): string {
  return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

/** The element's text without its aria-hidden parts, as a description reads it. */
function spoken(el: Element | null): string {
  const copy = el!.cloneNode(true) as Element;
  copy.querySelectorAll('[aria-hidden="true"]').forEach((hidden) => hidden.remove());
  return text(copy);
}

function idsIn(el: HTMLElement, selector: string): string[] {
  return [...el.querySelectorAll(`${selector} [data-issue-id]`)].map((c) =>
    c.getAttribute('data-issue-id')!,
  );
}

function minutesAgo(minutes: number): string {
  return new Date(Date.now() - minutes * 60_000).toISOString();
}

function activity(issueId: string, overrides: Partial<AgentActivity> = {}): AgentActivity {
  return {
    workerRunId: `run-${issueId}`,
    issueId,
    role: 'implement',
    lastEventAt: null,
    tasks: [],
    currentTask: null,
    completedTaskCount: 0,
    subagents: [],
    inFlightTool: null,
    tokens: { inputTokens: 0, outputTokens: 0 },
    backgroundTasks: [],
    ...overrides,
  };
}

describe('BoardPageComponent', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    document.body.innerHTML = '';
  });

  it('is a section labelled by the Board heading', async () => {
    // Act
    const { el } = await setup();

    // Assert
    const section = el.querySelector('section.board-page')!;
    expect(section.getAttribute('aria-labelledby')).toBe('board-heading');
    expect(el.querySelector('h1#board-heading')?.textContent).toBe('Board');
  });

  it('holds only the heading and the dispatch toggle in its header', async () => {
    // Act
    const { el } = await setup();

    // Assert
    const header = el.querySelector('.board-page__header')!;
    expect([...header.children].map((c) => c.tagName.toLowerCase())).toEqual([
      'h1',
      'fd-dispatch-controls',
    ]);
    expect(text(header.querySelector('fd-dispatch-controls'))).toBe('Pause all');
  });

  it('is busy while the issues load', async () => {
    // Act
    const { el } = await setup();

    // Assert
    const board = el.querySelector('.board')!;
    expect(board.getAttribute('role')).toBe('status');
    expect(board.getAttribute('aria-busy')).toBe('true');
    expect(board.textContent).toContain('Loading the board…');
  });

  it('shows the bands in priority order', async () => {
    // Act
    const { el } = await loaded([
      item('b', 1, {
        state: 'blocked',
        awaitsDispatch: true,
        runKind: 'fresh',
        waitsOnBlockers: true,
      }),
      item('p', 2, { state: 'queued', awaitsDispatch: true, runKind: 'fresh' }),
      item('w', 3),
      item('n', 4, { state: 'failed', needsAction: true }),
    ]);

    // Assert
    expect([...el.querySelectorAll('h2')].map((h) => h.id)).toEqual([
      'board-band-needs',
      'board-band-progress',
      'board-band-pipeline',
      'board-band-blocked',
    ]);
    expect(idsIn(el, '[aria-label="Issues that need you"]')).toEqual(['n']);
    expect(idsIn(el, '[aria-label="Issues in progress"]')).toEqual(['w']);
    expect(idsIn(el, '[aria-label="Pipeline in dispatch order"]')).toEqual(['p']);
    expect(idsIn(el, '[aria-label="Blocked issues"]')).toEqual(['b']);
  });

  it('labels each band by its heading', async () => {
    // Act
    const { el } = await loaded([item('w', 3)]);

    // Assert
    const sections = [...el.querySelectorAll('.board section')];
    expect(sections.map((s) => s.getAttribute('aria-labelledby'))).toEqual(['board-band-progress']);
  });

  describe('Needs you', () => {
    it('is hidden when nothing needs you', async () => {
      // Act
      const { el } = await loaded([item('w', 3)]);

      // Assert
      expect(el.querySelector('#board-band-needs')).toBeNull();
      expect(el.querySelector('.board-band--tinted')).toBeNull();
      expect(text(el)).not.toContain('Nothing needs you');
    });

    it('is the one tinted band, and counts its alerts and cards', async () => {
      // Act
      const { el } = await loaded([
        item('n', 4, { state: 'failed', needsAction: true }),
        item('q', 5, {
          state: 'queued',
          awaitsDispatch: true,
          runKind: 'fresh',
          repositoryEligibilityStatus: 'ineligible',
        }),
      ]);

      // Assert
      const tinted = el.querySelectorAll('.board-band--tinted');
      expect(tinted).toHaveLength(1);
      expect(tinted[0].getAttribute('aria-labelledby')).toBe('board-band-needs');
      expect(text(el.querySelector('.needs-band__count'))).toBe('2');
      expect(text(el.querySelector('#board-band-needs .sr-only'))).toBe(', 2 items');
    });

    it('shows one alert per ineligible repository with a link to its settings', async () => {
      // Arrange
      const h = await loaded([
        item('a', 4, {
          state: 'queued',
          awaitsDispatch: true,
          runKind: 'fresh',
          repositoryEligibilityStatus: 'ineligible',
          repositoryId: 'repo-9',
        }),
        item('b', 5, {
          state: 'queued',
          awaitsDispatch: true,
          runKind: 'fresh',
          repositoryEligibilityStatus: 'ineligible',
          repositoryId: 'repo-9',
        }),
      ]);

      // Act
      h.repositories.set([
        {
          id: 'repo-9',
          slug: 'acme/api',
          eligibility: {
            status: 'ineligible',
            violations: [{ rule: 'r', description: 'Require pull requests' }],
            reason: null,
          },
        } as RepositorySummary,
      ]);
      await h.render();
      await new Promise((resolve) => setTimeout(resolve, 200));
      await h.render();

      // Assert
      const alerts = h.el.querySelectorAll('[aria-label="Repository alerts"] fd-repo-alert');
      expect(alerts).toHaveLength(1);
      expect(text(alerts[0])).toContain("acme/api can't be worked on · 2 issues held");
      expect(text(alerts[0])).toContain('Require pull requests');
      expect(alerts[0].querySelector('a')?.getAttribute('href')).toBe(
        '/settings/repositories/repo-9',
      );
    });

    it('orders cards by tone, then longest waiting', async () => {
      // Act
      const { el } = await loaded([
        item('ready', 1, {
          stage: 'merge',
          state: 'review',
          needsAction: true,
          enteredStateAt: minutesAgo(90),
        }),
        item('new-failure', 2, {
          state: 'failed',
          needsAction: true,
          enteredStateAt: minutesAgo(1),
        }),
        item('old-failure', 3, {
          state: 'failed',
          needsAction: true,
          enteredStateAt: minutesAgo(30),
        }),
      ]);

      // Assert
      expect(idsIn(el, '[aria-label="Issues that need you"]')).toEqual([
        'old-failure',
        'new-failure',
        'ready',
      ]);
    });

    it('shows why, where it stopped and how long it has waited', async () => {
      // Act
      const { el } = await loaded([
        item('n', 42, {
          title: 'Add retry',
          repositorySlug: 'acme/api',
          state: 'awaiting_input',
          needsAction: true,
          enteredStateAt: minutesAgo(12),
        }),
      ]);

      // Assert
      const card = el.querySelector('fd-lifecycle-row')!;
      expect(text(card.querySelector('.lifecycle-row__reason'))).toBe('Needs answers');
      const ref = card.querySelector('fd-issue-ref.lifecycle-row__ref')!;
      expect(text(ref.querySelector('.issue-ref__name'))).toBe('api');
      expect(text(ref.querySelector('.issue-ref__number'))).toBe('#42');
      expect(ref.getAttribute('title')).toBe('acme/api');
      expect(text(card.querySelector('.lifecycle-row__why'))).toBe(
        'Questions are waiting for your answers',
      );
      expect(text(card.querySelector('time'))).toBe('waiting 12m');
    });

    it('opens the pane from its title and offers one action after it', async () => {
      // Act
      const { el } = await loaded([
        item('n', 42, { title: 'Add retry', state: 'failed', needsAction: true }),
      ]);

      // Assert
      const card = el.querySelector('fd-lifecycle-row')!;
      const buttons = card.querySelectorAll('button:not([hidden])');
      expect(buttons).toHaveLength(2);
      expect(buttons[0].getAttribute('aria-label')).toBe('Add retry, #42');
      expect(buttons[0].getAttribute('aria-haspopup')).toBe('dialog');
      expect(buttons[1].getAttribute('aria-label')).toBe('Retry issue #42');
      expect(buttons[1].hasAttribute('aria-haspopup')).toBe(false);
      expect(card.querySelector('a')).toBeNull();
    });

    it('opens a ready pull request in a new tab from its row button', async () => {
      // Act
      const { el } = await loaded([
        item('r', 7, {
          stage: 'merge',
          state: 'review',
          needsAction: true,
          providerType: 'gitlab',
          pullRequestUrl: 'https://gitlab.com/acme/api/-/merge_requests/3',
        }),
      ]);

      // Assert
      const link = el.querySelector<HTMLAnchorElement>('fd-lifecycle-row a')!;
      expect(link.getAttribute('href')).toBe('https://gitlab.com/acme/api/-/merge_requests/3');
      expect(link.getAttribute('target')).toBe('_blank');
      expect(link.getAttribute('rel')).toBe('noopener noreferrer');
      expect(text(link)).toBe('Open MR ↗');
      expect(link.getAttribute('aria-label')).toBe(
        'Open MR for issue #7 (opens GitLab in a new tab)',
      );
    });

    it('opens a pull request Foundry did not merge from its row button', async () => {
      // Act
      const { el } = await loaded([
        item('r', 7, {
          stage: 'merge',
          state: 'review',
          needsAction: true,
          providerType: 'github',
          mergeOutcome: 'skipped',
          mergeSkippedReason: 'not_mergeable',
          pullRequestUrl: 'https://github.com/acme/api/pull/3',
        }),
      ]);

      // Assert
      const link = el.querySelector<HTMLAnchorElement>('fd-lifecycle-row a')!;
      expect(link.getAttribute('href')).toBe('https://github.com/acme/api/pull/3');
      expect(text(link)).toBe('Open PR ↗');
    });

    it('keeps the review link and explains a review that did not finish', async () => {
      // Act
      const { el } = await loaded([
        item('r', 7, {
          stage: 'merge',
          state: 'review',
          needsAction: true,
          providerType: 'github',
          reviewOutcome: 'skipped',
          reviewSkippedReason: 'verdict_invalid',
          pullRequestUrl: 'https://github.com/acme/api/pull/3',
        }),
      ]);

      // Assert
      const card = el.querySelector('fd-lifecycle-row')!;
      expect(text(card.querySelector('.lifecycle-row__why'))).toBe(
        "Checks passed; the review didn't finish: invalid verdict",
      );
      expect(text(card.querySelector('a'))).toBe('Review PR ↗ (opens GitHub in a new tab)');
    });

    it('shows its lifecycle rail with the current step marked', async () => {
      // Act
      const { el } = await loaded([
        item('f', 8, {
          stage: 'review',
          state: 'review_failed',
          needsAction: false,
          runKind: null,
          failureClassification: 'transient_api_error',
          lifecycle: [
            { step: 'implement', actor: 'claude', status: 'done', phase: null },
            { step: 'review', actor: 'claude', status: 'current', phase: 'retrying' },
            { step: 'your_review', actor: 'person', status: 'upcoming', phase: null },
          ],
        }),
      ]);

      // Assert
      const rail = el.querySelector(
        'fd-lifecycle-row fd-lifecycle-rail ol[aria-label="Lifecycle"]',
      )!;
      expect(rail.querySelector('[aria-current="step"]')?.getAttribute('aria-label')).toBe(
        'Review, by Claude, current, retrying',
      );
      expect(rail.querySelectorAll('li')).toHaveLength(3);
    });

    it('shows no rail when the server serves no lifecycle', async () => {
      // Act
      const { el } = await loaded([item('n', 4, { state: 'failed', needsAction: true })]);

      // Assert
      expect(el.querySelector('fd-lifecycle-row')).not.toBeNull();
      expect(el.querySelector('fd-lifecycle-rail')).toBeNull();
    });
  });

  describe('In progress', () => {
    it('shows a running row with its run kind, number and elapsed time', async () => {
      // Act
      const { el } = await loaded([
        item('w', 3, {
          stage: 'triage',
          state: 'triage_in_progress',
          runKind: 'triage',
          enteredStateAt: minutesAgo(12),
        }),
      ]);

      // Assert
      const row = el.querySelector('fd-lifecycle-row')!;
      expect(text(row.querySelector('fd-run-kind-tag'))).toBe('Run: triage');
      expect(text(row.querySelector('.lifecycle-row__head fd-issue-ref .issue-ref__number'))).toBe(
        '#3',
      );
      expect(text(row.querySelector('.lifecycle-row__status .lifecycle-row__clock'))).toBe(
        'Running for running 12m',
      );
      expect(row.querySelector('a')).toBeNull();
    });

    it('shows a running row with an unknown run kind, without a tag', async () => {
      // Act
      const { el } = await loaded([item('w', 3, { runKind: 'deploy' })]);

      // Assert
      expect(idsIn(el, '[aria-label="Issues in progress"]')).toEqual(['w']);
      expect(el.querySelector('fd-run-kind-tag')).toBeNull();
    });

    it('loops only the current node of a running row', async () => {
      // Act
      const { el } = await loaded([
        item('w', 3, {
          lifecycle: [{ step: 'implement', actor: 'claude', status: 'current', phase: 'running' }],
        }),
        item('m', 4, {
          state: 'merge_pending',
          stage: 'merge',
          lifecycle: [{ step: 'merge', actor: 'code', status: 'current', phase: 'waiting' }],
        }),
      ]);

      // Assert
      const running = [...el.querySelectorAll('.lifecycle-row--running')].map((r) =>
        r.closest('fd-lifecycle-row')!.getAttribute('data-issue-id'),
      );
      expect(running).toEqual(['w']);
    });

    describe('worker activity', () => {
      const current = {
        id: '2',
        subject: 'Run tests',
        activeForm: 'Running tests',
        status: 'in_progress',
      };
      const running = (): AgentActivity =>
        activity('w', {
          lastEventAt: minutesAgo(2.5),
          tasks: [
            { id: '1', subject: 'Plan', activeForm: null, status: 'completed' },
            current,
            { id: '3', subject: 'Write docs', activeForm: null, status: 'pending' },
          ],
          currentTask: current,
          completedTaskCount: 1,
        });

      it('shows the starting state before the first activity event', async () => {
        // Act
        const { el } = await loaded([item('w', 3)]);

        // Assert
        const panel = el.querySelector('fd-worker-activity')!;
        expect([...panel.querySelectorAll('p')].map((line) => text(line))).toEqual([
          "Waiting for the agent's first event",
        ]);
        expect(el.querySelector('.last-event__event')).toBeNull();
        expect(el.querySelector('.lifecycle-row__sep')).toBeNull();
      });

      it('shows the current task and its position on a hidden segmented bar, with no tool line', async () => {
        // Arrange
        const h = await loaded([item('w', 3)]);

        // Act
        h.activities.set(
          new Map([
            [
              'w',
              {
                ...running(),
                inFlightTool: { name: 'Bash' },
              },
            ],
          ]),
        );
        await h.render();

        // Assert
        const panel = h.el.querySelector('fd-worker-activity')!;
        expect(text(panel.querySelector('.worker-activity__task'))).toBe('Running tests');
        expect(text(panel.querySelector('.worker-activity__progress'))).toBe('task 2 of 3');
        const bar = panel.querySelector('.worker-activity__bar')!;
        expect(bar.getAttribute('aria-hidden')).toBe('true');
        expect(
          [...bar.querySelectorAll('.worker-activity__segment')].map((s) => s.className),
        ).toEqual([
          'worker-activity__segment worker-activity__segment--done',
          'worker-activity__segment worker-activity__segment--current',
          'worker-activity__segment',
        ]);
        expect(text(panel)).not.toContain('npm test');
      });

      it('has no bar without a task list', async () => {
        // Arrange
        const h = await loaded([item('w', 3)]);

        // Act
        h.activities.set(new Map([['w', activity('w')]]));
        await h.render();

        // Assert
        const panel = h.el.querySelector('fd-worker-activity')!;
        expect(panel.querySelector('.worker-activity__bar')).toBeNull();
        expect([...panel.querySelectorAll('p')].map((line) => text(line))).toEqual(['Working']);
      });

      it('describes the card button with the task line, without a live region', async () => {
        // Arrange
        const h = await loaded([item('w', 3)]);

        // Act
        h.activities.set(new Map([['w', running()]]));
        await h.render();

        // Assert
        const button = h.el.querySelector('.lifecycle-row__title')!;
        const ids = button.getAttribute('aria-describedby')!.split(' ');
        expect(ids.map((id) => text(h.el.querySelector(`[id="${id}"]`)))).toContain(
          'Running tests',
        );
        const card = h.el.querySelector('fd-lifecycle-row')!;
        expect(card.querySelector('[aria-live], [role="status"]')).toBeNull();
      });

      it('describes the card button with its top line, task line and status while live', async () => {
        // Arrange
        const h = await loaded([item('w', 3, { enteredStateAt: minutesAgo(12) })]);

        // Act
        h.activities.set(new Map([['w', running()]]));
        await h.render();

        // Assert
        const button = h.el.querySelector('.lifecycle-row__title')!;
        expect(button.getAttribute('aria-describedby')).toBe(
          'lifecycle-row-w-ref lifecycle-row-w-task lifecycle-row-w-clock',
        );
        expect(spoken(h.el.querySelector('[id="lifecycle-row-w-ref"]'))).toMatch(/acme\/api ?#3/);
        expect(spoken(h.el.querySelector('[id="lifecycle-row-w-clock"]'))).toBe(
          'Running for 12m, last event 2m ago',
        );
      });

      it('describes a completed card by its top line and status, which reads Completed', async () => {
        // Arrange
        const h = await loaded([item('w', 3)]);

        // Act
        hubHandlers.get('IssueUpdated')!(item('w', 3, { state: 'completed', stage: 'done' }));
        await h.render();

        // Assert
        const card = h.el.querySelector('fd-lifecycle-row')!;
        const button = card.querySelector('.lifecycle-row__title')!;
        expect(button.getAttribute('aria-describedby')).toBe(
          'lifecycle-row-w-ref lifecycle-row-w-status',
        );
        expect(text(card.querySelector('[id="lifecycle-row-w-status"]'))).toBe('Completed');
        expect(card.querySelector('fd-worker-activity')).toBeNull();
      });

      it('says when the last event was', async () => {
        // Arrange
        const h = await loaded([item('w', 3)]);

        // Act
        h.activities.set(new Map([['w', running()]]));
        await h.render();

        // Assert
        const event = h.el.querySelector('.lifecycle-row__status .last-event__event')!;
        expect(text(event)).toBe('last event 2m ago');
        expect(h.el.querySelector('.lifecycle-row__sep')?.getAttribute('aria-hidden')).toBe('true');
        expect(h.el.querySelector('.lifecycle-row[data-tone="warning"]')).toBeNull();
      });

      it('turns the card silent at the silence threshold', async () => {
        // Arrange
        const h = await loaded([item('w', 3)]);

        // Act
        h.activities.set(
          new Map([
            [
              'w',
              activity('w', {
                lastEventAt: minutesAgo(6.5),
                inFlightTool: { name: 'Bash' },
              }),
            ],
          ]),
        );
        await h.render();

        // Assert
        const event = h.el.querySelector('.last-event__event')!;
        expect(text(event)).toBe('silent 6m · Bash running');
        expect(event.classList).toContain('last-event__event--silent');
        expect(event.querySelector('svg[aria-hidden="true"]')).not.toBeNull();
        expect(h.el.querySelector('.lifecycle-row[data-tone="warning"]')).not.toBeNull();
      });

      it('names the background task it waits on instead of turning silent', async () => {
        // Arrange
        const h = await loaded([item('w', 3)]);

        // Act
        h.activities.set(
          new Map([
            [
              'w',
              activity('w', {
                lastEventAt: minutesAgo(7.5),
                inFlightTool: { name: 'Bash' },
                backgroundTasks: [
                  { taskType: 'local_bash', description: 'dotnet test' },
                  { taskType: 'local_bash', description: 'npm test' },
                  { taskType: 'local_bash', description: '' },
                ],
              }),
            ],
          ]),
        );
        await h.render();

        // Assert
        const waiting = h.el.querySelector('.lifecycle-row__status .last-event__waiting')!;
        expect(waiting.getAttribute('aria-hidden')).toBe('true');
        expect(text(waiting)).toBe('waiting on dotnet test +2 more · 7m');
        expect(text(waiting.querySelector('.last-event__command'))).toBe('dotnet test');
        expect(waiting.getAttribute('title')).toBe('dotnet test\nnpm test\na background task');
        expect(text(h.el.querySelector('.lifecycle-row__status .sr-only:last-child'))).toBe(
          'waiting on 3 background tasks, 7m',
        );
        expect(h.el.querySelector('.last-event__event')).toBeNull();
        expect(h.el.querySelector('.last-event__warning')).toBeNull();
        expect(h.el.querySelector('.lifecycle-row[data-tone="warning"]')).toBeNull();
      });

      it('keeps the last-event line below the threshold while background tasks run', async () => {
        // Arrange
        const h = await loaded([item('w', 3)]);

        // Act
        h.activities.set(
          new Map([
            [
              'w',
              activity('w', {
                lastEventAt: minutesAgo(2.5),
                backgroundTasks: [{ taskType: 'local_bash', description: 'dotnet test' }],
              }),
            ],
          ]),
        );
        await h.render();

        // Assert
        expect(text(h.el.querySelector('.last-event__event'))).toBe('last event 2m ago');
        expect(h.el.querySelector('.last-event__waiting')).toBeNull();
      });

      it("keeps the summary's run kind when the run's activity arrives", async () => {
        // Arrange
        const h = await loaded([item('w', 3, { runKind: 'fresh' })]);

        // Act
        h.activities.set(new Map([['w', activity('w', { role: 'revise' })]]));
        await h.render();

        // Assert
        expect(text(h.el.querySelector('fd-lifecycle-row fd-run-kind-tag'))).toBe('Run: implement');
      });
    });

    it('shows a step Foundry runs as a row in the same list, saying what it does', async () => {
      // Act
      const { el } = await loaded([
        item('s', 3, {
          state: 'checks_pending',
          lifecycle: [{ step: 'checks', actor: 'code', status: 'current', phase: 'running' }],
        }),
      ]);

      // Assert
      expect(el.querySelector('h3')).toBeNull();
      expect(idsIn(el, '[aria-label="Issues in progress"]')).toEqual(['s']);
      expect(text(el.querySelector('.lifecycle-row__doing'))).toBe('Waiting for checks');
    });

    it('shows the slots in use in its heading', async () => {
      // Arrange
      const h = await loaded([item('w', 3)]);

      // Act
      h.capacity.set({ maxConcurrent: 2, occupiedSlots: 2, freeSlots: 0 });
      await h.render();

      // Assert
      expect(text(h.el.querySelector('#board-band-progress'))).toBe('In progress·, 2 of 2 slots');
      expect(h.el.querySelectorAll('.progress-band__pip--busy')).toHaveLength(2);
    });

    it('says the slots are unavailable when capacity fails to load', async () => {
      // Arrange
      const h = await loaded([item('w', 3)]);

      // Act
      h.capacityError.set('Failed to load dispatch capacity');
      await h.render();

      // Assert
      expect(text(h.el.querySelector('#board-band-progress'))).toBe(
        'In progress·, Slots unavailable',
      );
    });

    it('is hidden when nothing runs', async () => {
      // Act
      const { el } = await loaded([
        item('a', 1, { state: 'queued', awaitsDispatch: true, runKind: 'fresh' }),
        item('n', 4, { state: 'failed', needsAction: true }),
      ]);

      // Assert
      expect(el.querySelector('#board-band-progress')).toBeNull();
      expect(el.querySelector('.progress-band__pips')).toBeNull();
    });
  });

  describe('Pipeline', () => {
    it('lists every queued issue in global dispatch order with its position', async () => {
      // Act
      const { el } = await loaded([
        item('t', 1, {
          stage: 'triage',
          state: 'triage_queued',
          awaitsDispatch: true,
          runKind: 'triage',
        }),
        item('d', 2, {
          stage: 'triage',
          state: 'detected',
          awaitsDispatch: true,
          runKind: 'triage',
        }),
        item('x', 3, {
          state: 'queued',
          awaitsDispatch: true,
          runKind: 'fresh',
          repositoryEligibilityStatus: 'unreachable',
        }),
        item('i', 4, { state: 'queued', awaitsDispatch: true, runKind: 'fresh' }),
      ]);

      // Assert
      const rows = [
        ...el.querySelectorAll('ol[aria-label="Pipeline in dispatch order"] fd-board-row'),
      ];
      expect(rows.map((r) => r.getAttribute('data-issue-id'))).toEqual(['t', 'i', 'd', 'x']);
      expect(rows.map((r) => text(r.querySelector('.pipeline-band__position')))).toEqual([
        'Position #1 next',
        'Position #2',
        '',
        '—',
      ]);
      expect(text(rows[2].querySelector('.board-row__marker'))).toBe('Detected, no position yet');
      expect(text(rows[3].querySelector('.board-row__marker'))).toBe('Repo ineligible');
      expect(rows[0].querySelector('.board-row--next')).not.toBeNull();
      expect(text(el.querySelector('.pipeline-band__count'))).toBe('·, 4 queued');
    });

    it('names each row by its title and number, with the full slug in its description', async () => {
      // Act
      const { el } = await loaded([
        item('i', 32, {
          title: 'Fix alerts',
          state: 'queued',
          awaitsDispatch: true,
          runKind: 'fresh',
          repositorySlug: 'acme/isal-monitoring',
        }),
      ]);

      // Assert
      const row = el.querySelector('fd-board-row')!;
      const button = row.querySelector('button')!;
      expect(button.getAttribute('aria-label')).toBe('Fix alerts, #32');
      const repo = row.querySelector('fd-issue-ref.board-row__repo')!;
      expect(repo.id).toBe('board-row-i-repo');
      expect(repo.getAttribute('title')).toBe('acme/isal-monitoring');
      expect(button.getAttribute('aria-describedby')).toContain(repo.id);
      expect(text(repo.querySelector('[aria-hidden="true"]'))).toBe('isal-monitoring');
    });

    it('shows the first 10 rows until Show all', async () => {
      // Arrange
      const queued = Array.from({ length: 12 }, (_, i) =>
        item(`q${i}`, i + 1, { state: 'queued', awaitsDispatch: true, runKind: 'fresh' }),
      );
      const h = await loaded(queued);
      expect(h.el.querySelectorAll('fd-board-row')).toHaveLength(10);
      const more = h.el.querySelector<HTMLButtonElement>('.pipeline-band__more')!;
      expect(text(more)).toBe('Show all 12');

      // Act
      more.click();
      await h.render();

      // Assert
      expect(h.el.querySelectorAll('fd-board-row')).toHaveLength(12);
      expect(text(more)).toBe('Show first 10');
    });

    it('says the queue order may be out of date beside its heading', async () => {
      // Arrange
      const h = await loaded([
        item('i', 4, { state: 'queued', awaitsDispatch: true, runKind: 'fresh' }),
      ]);

      // Act
      (
        TestBed.inject(IssueService) as unknown as {
          _queueOrderStaleSignal: WritableSignal<boolean>;
        }
      )._queueOrderStaleSignal.set(true);
      await h.render();

      // Assert
      expect(text(h.el.querySelector('.pipeline-band__stale'))).toBe(
        'Queue order may be out of date',
      );
    });

    it('shows a lifecycle bar and one status phrase on a pipeline row', async () => {
      // Act
      const { el } = await loaded([
        item('r', 1030, {
          state: 'revision_queued',
          awaitsDispatch: true,
          runKind: 'checks_fix',
          round: 2,
          lifecycle: [
            { step: 'implement', actor: 'claude', status: 'done', phase: null },
            { step: 'queued', actor: 'code', status: 'current', phase: 'waiting' },
            { step: 'implement', actor: 'claude', status: 'upcoming', phase: null },
          ],
        }),
      ]);

      // Assert
      const row = el.querySelector('ol[aria-label="Pipeline in dispatch order"] fd-board-row')!;
      expect(text(row.querySelector('.board-row__lead'))).toBe('Position #1 next');
      expect(
        row.querySelector('.board-row__bar fd-lifecycle-bar')?.getAttribute('aria-hidden'),
      ).toBe('true');
      expect(row.querySelector('fd-lifecycle-rail')).toBeNull();
      expect(text(row.querySelector('.board-row__marker'))).toBe('Queued for implement · round 2');
      expect(row.querySelector('.board-row__marker [title]')?.getAttribute('title')).toBe(
        'Queued for implement · round 2',
      );
    });

    it('is hidden with no rows', async () => {
      // Act
      const { el } = await loaded([item('w', 3)]);

      // Assert
      expect(el.querySelector('fd-pipeline-band')).toBeNull();
    });
  });

  describe('Blocked', () => {
    const blocked = () => [
      item('b', 1, {
        state: 'blocked',
        awaitsDispatch: true,
        runKind: 'fresh',
        waitsOnBlockers: true,
      }),
    ];

    it('is collapsed by default behind a disclosure in its heading', async () => {
      // Act
      const { el } = await loaded(blocked());

      // Assert
      const button = el.querySelector<HTMLButtonElement>('#board-band-blocked button')!;
      expect(text(button)).toBe('Blocked 1');
      expect(button.getAttribute('aria-expanded')).toBe('false');
      expect(button.getAttribute('aria-controls')).toBe('board-blocked-list');
      expect(el.querySelector('#board-blocked-list')?.hasAttribute('hidden')).toBe(true);
      expect(text(el.querySelector('.blocked-band__aside'))).toBe(
        'These start on their own once their blockers close.',
      );
    });

    it('leads each row with its run kind, not its role', async () => {
      // Act
      const { el } = await loaded([
        item('b', 1, {
          stage: 'triage',
          state: 'triage_blocked',
          awaitsDispatch: true,
          runKind: 'triage',
          waitsOnBlockers: true,
        }),
      ]);

      // Assert
      const lead = el.querySelector('#board-blocked-list fd-board-row .board-row__lead');
      expect(text(lead?.querySelector('fd-run-kind-tag'))).toBe('Run: triage');
      expect(lead?.querySelector('fd-role-tag')).toBeNull();
    });

    it('expands and remembers it', async () => {
      // Arrange
      const h = await loaded(blocked());
      const button = h.el.querySelector<HTMLButtonElement>('#board-band-blocked button')!;

      // Act
      button.click();
      await h.render();

      // Assert
      expect(button.getAttribute('aria-expanded')).toBe('true');
      expect(h.el.querySelector('#board-blocked-list')?.hasAttribute('hidden')).toBe(false);
      expect(localStorage.getItem('fd.board.blocked-open')).toBe('true');
    });

    it('opens expanded when it was left expanded', async () => {
      // Arrange
      localStorage.setItem('fd.board.blocked-open', 'true');

      // Act
      const { el } = await loaded(blocked());

      // Assert
      expect(el.querySelector('#board-blocked-list')?.hasAttribute('hidden')).toBe(false);
    });

    it('stays collapsed when storage fails', async () => {
      // Arrange
      vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
        throw new Error('denied');
      });

      // Act
      const { el } = await loaded(blocked());

      // Assert
      expect(el.querySelector('#board-blocked-list')?.hasAttribute('hidden')).toBe(true);
    });

    it('is hidden with no blocked issues', async () => {
      // Act
      const { el } = await loaded([item('w', 3)]);

      // Assert
      expect(el.querySelector('fd-blocked-band')).toBeNull();
    });
  });

  it('shows one line naming the trigger label when nothing is in flight', async () => {
    // Act
    const { el } = await loaded([]);

    // Assert
    expect(el.querySelector('.board')).toBeNull();
    expect(text(el.querySelector('.board-page__empty'))).toBe(
      'Nothing is in flight. Add the foundry label to an issue to start work.',
    );
    expect(el.querySelector('.board-page__empty code')?.textContent).toBe('foundry');
  });

  it('mirrors the empty state into a status region', async () => {
    const { el } = await loaded([]);
    const regions = [...el.querySelectorAll('[role="status"]')].map((r) => r.textContent);
    expect(regions).toContain(
      'Nothing is in flight. Add the foundry label to an issue to start work.',
    );
  });

  it('shows an alert with a retry when the issues fail to load', async () => {
    // Act
    const h = await setup();
    vi.spyOn(console, 'error').mockImplementation(() => {});
    h.httpMock.expectOne('/api/issues').flush('boom', { status: 500, statusText: 'Server Error' });
    await h.render();

    // Assert
    expect(text(h.el.querySelector('[role="alert"]'))).toBe("Couldn't load the board.Retry");
  });

  it('reloads and focuses the heading on retry', async () => {
    // Arrange
    const h = await setup();
    vi.spyOn(console, 'error').mockImplementation(() => {});
    h.httpMock.expectOne('/api/issues').flush('boom', { status: 500, statusText: 'Server Error' });
    await h.render();

    // Act
    (h.el.querySelector('.board-page__retry') as HTMLButtonElement).click();

    // Assert
    h.httpMock.expectOne('/api/issues');
    expect(document.activeElement?.id).toBe('board-heading');
  });

  describe('connection gate', () => {
    /** Raises the gate, runs the page's reload with `items` as the fresh issues, and lowers it. */
    async function reconnect(h: Harness, items: unknown[]): Promise<LoadOutcome> {
      h.gate.phase.set('reconnecting');
      await h.render();
      h.gate.phase.set('reloading');
      await h.render();
      const reloading = h.gate.registered!();
      h.httpMock.expectOne('/api/issues').flush({ items, nextCursor: null });
      const outcome = await reloading;
      h.gate.phase.set('live');
      await h.render();
      return outcome;
    }

    function announced(h: Harness): string {
      return text(h.el.querySelector('fd-connection-gate > [aria-live="polite"]'));
    }

    it('hides the board, keeping no last state, while the gate is up', async () => {
      // Arrange
      const h = await loaded([item('f', 2)]);

      // Act
      h.gate.phase.set('reconnecting');
      await h.render();

      // Assert
      expect(h.el.querySelector('.board')).toBeNull();
      expect(h.el.querySelector('#board-heading')).toBeNull();
      expect(text(h.el.querySelector('.connection-gate__heading'))).toBe(
        'Reconnecting to Foundry…',
      );
      expect(h.el.textContent).not.toContain('showing last known state');
    });

    it('reloads the issues, capacity, dispatch settings and repositories, then renders afresh', async () => {
      // Arrange
      const h = await loaded([item('f', 2)]);

      // Act
      const outcome = await reconnect(h, [item('g', 3)]);

      // Assert
      expect(outcome).toBe('loaded');
      expect(h.loadSettings).toHaveBeenCalledTimes(1);
      expect(idsIn(h.el, '.board')).toEqual(['g']);
    });

    it('reports a failed reload when any of its loads fails', async () => {
      // Arrange
      const h = await loaded([item('f', 2)]);
      h.loadSettings.mockReturnValue(Promise.resolve('failed'));

      // Act
      const outcome = await reconnect(h, [item('f', 2)]);

      // Assert
      expect(outcome).toBe('failed');
    });

    it('says the board is up to date and how many need you', async () => {
      // Arrange
      const h = await loaded([item('f', 2)]);

      // Act
      await reconnect(h, [
        item('f', 2, { needsAction: true, state: 'failed' }),
        item('g', 3, { needsAction: true, state: 'failed' }),
      ]);

      // Assert
      expect(announced(h)).toBe('Board is up to date. 2 need you.');
    });

    it('says nothing needs you when nothing does', async () => {
      // Arrange
      const h = await loaded([item('f', 2)]);

      // Act
      await reconnect(h, [item('f', 2)]);

      // Assert
      expect(announced(h)).toBe('Board is up to date. Nothing needs you.');
    });

    it('reopens the pane when its issue is still on the board', async () => {
      // Arrange
      const h = await loaded([item('f', 142)], '/?issue=f');
      h.httpMock.match('/api/issues/f');

      // Act
      await reconnect(h, [item('f', 142)]);

      // Assert
      h.httpMock.match('/api/issues/f');
      expect(TestBed.inject(Router).url).toBe('/?issue=f');
      expect(h.el.querySelector('[role="dialog"]')).not.toBeNull();
    });

    it('leaves the URL alone when the board is left while it reloads', async () => {
      // Arrange
      const h = await loaded([item('f', 142), item('g', 7)], '/?issue=f');
      h.httpMock.match('/api/issues/f');
      h.gate.phase.set('reconnecting');
      await h.render();
      h.gate.phase.set('reloading');
      await h.render();
      const reloading = h.gate.registered!();
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate');

      // Act
      h.fixture.destroy();
      h.httpMock.expectOne('/api/issues').flush({ items: [item('g', 7)], nextCursor: null });
      await reloading;

      // Assert
      expect(navigate).not.toHaveBeenCalled();
    });

    it('closes the pane and says so when its issue left the board', async () => {
      // Arrange
      const h = await loaded([item('f', 142), item('g', 7)], '/?issue=f');
      h.httpMock.match('/api/issues/f');

      // Act
      await reconnect(h, [item('g', 7)]);

      // Assert
      expect(TestBed.inject(Router).url).toBe('/');
      expect(h.el.querySelector('[role="dialog"].side-sheet:not(.side-sheet--leave)')).toBeNull();
      expect(announced(h)).toBe(
        'Issue #142 is no longer on the board. Board is up to date. Nothing needs you.',
      );
    });
  });

  describe('pane', () => {
    async function openCard(h: Harness, id: string): Promise<HTMLButtonElement> {
      const button = h.el.querySelector(`[data-issue-id="${id}"] button`) as HTMLButtonElement;
      button.click();
      await h.render();
      return button;
    }

    it('opens the issue detail in a dialog headed by the issue', async () => {
      // Arrange
      const h = await loaded([item('f', 142, { title: 'Add retry' })]);

      // Act
      await openCard(h, 'f');

      // Assert
      h.httpMock.expectOne('/api/issues/f');
      const dialog = h.el.querySelector('[role="dialog"]')!;
      expect(dialog.querySelector('h2')?.textContent?.trim()).toBe('#142 Add retry');
      // The status layer leads the pane and shows its skeleton until the detail lands (#862).
      expect(dialog.querySelector('fd-pane-status')).not.toBeNull();
    });

    it('moves focus into the pane when it opens', async () => {
      // Arrange
      const h = await loaded([item('f', 142)]);

      // Act
      await openCard(h, 'f');

      // Assert
      h.httpMock.expectOne('/api/issues/f');
      expect(document.activeElement?.id).toBe('issue-pane-title');
    });

    it('returns focus to the card when Escape closes it', async () => {
      // Arrange
      const h = await loaded([item('f', 142)]);
      const button = await openCard(h, 'f');
      h.httpMock.expectOne('/api/issues/f');

      // Act
      document.activeElement!.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }),
      );
      await h.render();

      // Assert
      expect(document.activeElement).toBe(button);
    });

    it('closes on a click outside it', async () => {
      // Arrange
      const h = await loaded([item('f', 142)]);
      await openCard(h, 'f');
      h.httpMock.expectOne('/api/issues/f');

      // Act
      h.el.querySelector('h1')!.dispatchEvent(new Event('pointerdown', { bubbles: true }));
      await h.render();
      await new Promise((resolve) => setTimeout(resolve, 0));
      await h.render();

      // Assert
      expect(h.el.querySelector('[role="dialog"].side-sheet:not(.side-sheet--leave)')).toBeNull();
    });

    it('puts the open issue in the URL', async () => {
      // Arrange
      const h = await loaded([item('f', 142)]);

      // Act
      await openCard(h, 'f');

      // Assert
      h.httpMock.expectOne('/api/issues/f');
      expect(TestBed.inject(Router).url).toBe('/?issue=f');
    });

    it('closes on browser Back and returns focus to the card', async () => {
      // Arrange
      const h = await loaded([item('f', 142)]);
      const button = await openCard(h, 'f');
      h.httpMock.expectOne('/api/issues/f');

      // Act
      TestBed.inject(Location).back();
      await h.render();

      // Assert
      expect(TestBed.inject(Router).url).toBe('/');
      expect(h.el.querySelector('[role="dialog"].side-sheet:not(.side-sheet--leave)')).toBeNull();
      expect(document.activeElement).toBe(button);
    });

    it('removes the parameter when the close button closes it', async () => {
      // Arrange
      const h = await loaded([item('f', 142)]);
      await openCard(h, 'f');
      h.httpMock.expectOne('/api/issues/f');

      // Act
      (h.el.querySelector('button[aria-label="Close issue details"]') as HTMLButtonElement).click();
      await h.render();

      // Assert
      expect(TestBed.inject(Router).url).toBe('/');
    });

    it('swaps without adding a history entry, so Back closes the pane', async () => {
      // Arrange
      const h = await loaded([item('a', 1), item('b', 2)]);
      await openCard(h, 'a');
      h.httpMock.expectOne('/api/issues/a');
      await openCard(h, 'b');
      h.httpMock.expectOne('/api/issues/b');
      expect(TestBed.inject(Router).url).toBe('/?issue=b');

      // Act
      TestBed.inject(Location).back();
      await h.render();

      // Assert
      expect(TestBed.inject(Router).url).toBe('/');
    });

    it('moves focus to the heading when it swaps', async () => {
      // Arrange
      const h = await loaded([item('a', 1), item('b', 2, { title: 'Second' })]);
      await openCard(h, 'a');
      h.httpMock.expectOne('/api/issues/a');

      // Act
      await openCard(h, 'b');
      h.httpMock.expectOne('/api/issues/b');

      // Assert
      expect(document.activeElement?.id).toBe('issue-pane-title');
      expect(document.activeElement?.textContent?.trim()).toBe('#2 Second');
    });

    it('reopens from a deep link once the board has loaded', async () => {
      // Arrange
      const h = await setup('/?issue=f');
      expect(h.el.querySelector('[role="dialog"]')).toBeNull();

      // Act
      h.httpMock
        .expectOne('/api/issues')
        .flush({ items: [item('f', 142, { title: 'Linked' })], nextCursor: null });
      await h.render();

      // Assert
      h.httpMock.expectOne('/api/issues/f');
      expect(h.el.querySelector('[role="dialog"] h2')?.textContent?.trim()).toBe('#142 Linked');
      expect(TestBed.inject(Router).url).toBe('/?issue=f');
    });

    it('removes a deep link to an issue not on the board and opens nothing', async () => {
      // Act
      const h = await loaded([item('f', 142)], '/?issue=gone');

      // Assert
      h.httpMock.expectNone('/api/issues/gone');
      expect(h.el.querySelector('[role="dialog"]')).toBeNull();
      expect(TestBed.inject(Router).url).toBe('/');
    });

    it('stays open on an issue that completes while it is open', async () => {
      // Arrange
      const h = await loaded([item('f', 142, { title: 'Add retry' })]);
      await openCard(h, 'f');
      h.httpMock.expectOne('/api/issues/f');

      // Act
      hubHandlers.get('IssueUpdated')!(
        item('f', 142, { title: 'Add retry', state: 'completed', stage: 'done' }),
      );
      await h.render();

      // Assert
      h.httpMock.match('/api/issues/f');
      expect(h.el.querySelector('[role="dialog"] h2')?.textContent?.trim()).toBe('#142 Add retry');
      expect(TestBed.inject(Router).url).toBe('/?issue=f');
    });

    it('returns focus to the band heading when the card moved into the collapsed Blocked band', async () => {
      // Arrange
      const h = await loaded([item('f', 142)]);
      await openCard(h, 'f');
      h.httpMock.expectOne('/api/issues/f');
      TestBed.inject(IssueService).issues.set([
        item('f', 142, {
          state: 'blocked',
          awaitsDispatch: true,
          runKind: 'fresh',
          waitsOnBlockers: true,
        }) as never,
      ]);
      await new Promise((resolve) => setTimeout(resolve, 200));
      await h.render();

      // Act
      document.activeElement!.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }),
      );
      await h.render();

      // Assert
      h.httpMock.match('/api/issues/f');
      expect(document.activeElement).toBe(h.el.querySelector('#board-band-blocked button'));
    });

    it('swaps to another card without closing', async () => {
      // Arrange
      const h = await loaded([item('a', 1, { title: 'First' }), item('b', 2, { title: 'Second' })]);
      await openCard(h, 'a');
      h.httpMock.expectOne('/api/issues/a');

      // Act
      h.el
        .querySelector('[data-issue-id="b"] button')!
        .dispatchEvent(new Event('pointerdown', { bubbles: true }));
      await openCard(h, 'b');

      // Assert
      h.httpMock.expectOne('/api/issues/b');
      expect(h.el.querySelector('[role="dialog"] h2')?.textContent?.trim()).toBe('#2 Second');
    });
  });
});
