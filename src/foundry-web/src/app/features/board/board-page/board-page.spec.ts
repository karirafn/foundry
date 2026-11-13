import { TestBed } from '@angular/core/testing';
import { WritableSignal, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { Location } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { vi } from 'vitest';
import {
  ConnectionStatus,
  IssueSignalRService,
} from '../../../core/services/issue-signalr.service';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { DispatchService } from '../../../core/services/dispatch.service';
import { DispatchCapacityService } from '../../floor/dispatch-capacity.service';
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
    codeStep: null,
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
  status: WritableSignal<ConnectionStatus>;
  repositories: WritableSignal<RepositorySummary[]>;
  capacity: WritableSignal<{
    maxConcurrent: number;
    occupiedSlots: number;
    freeSlots: number;
  } | null>;
  capacityError: WritableSignal<string | null>;
  render: () => Promise<void>;
}

async function setup(url = '/'): Promise<Harness> {
  const status = signal<ConnectionStatus>('connected');
  const repositories = signal<RepositorySummary[]>([]);
  const capacity = signal<{
    maxConcurrent: number;
    occupiedSlots: number;
    freeSlots: number;
  } | null>(null);
  const capacityError = signal<string | null>(null);
  TestBed.configureTestingModule({
    imports: [BoardPageComponent],
    providers: [
      provideRouter([]),
      provideLocationMocks(),
      provideHttpClient(),
      provideHttpClientTesting(),
      {
        provide: IssueSignalRService,
        useValue: {
          on: (name: string, handler: (payload: unknown) => void) => hubHandlers.set(name, handler),
          off: () => {},
          onReconnected: () => {},
          connectionStatus: status,
        },
      },
      {
        provide: WorkerSignalRService,
        useValue: {
          activeAgentCount: signal(0),
          activityForIssue: () => null,
          agentActivityForIssue: () => null,
        },
      },
      {
        provide: DispatchCapacityService,
        useValue: { capacity, loadError: capacityError, load: () => {} },
      },
      {
        provide: AccountService,
        useValue: { loadAccounts: () => Promise.resolve(), accounts: signal([]) },
      },
      {
        provide: RepositoryService,
        useValue: { repositories, loadAllRepositories: () => {} },
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
    status,
    repositories,
    capacity,
    capacityError,
    render,
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

function idsIn(el: HTMLElement, selector: string): string[] {
  return [...el.querySelectorAll(`${selector} [data-issue-id]`)].map((c) =>
    c.getAttribute('data-issue-id')!,
  );
}

function minutesAgo(minutes: number): string {
  return new Date(Date.now() - minutes * 60_000).toISOString();
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
      item('b', 1, { state: 'blocked', awaitsDispatch: true, waitsOnBlockers: true }),
      item('p', 2, { state: 'queued', awaitsDispatch: true }),
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
    expect(idsIn(el, '[aria-label="Running workers"]')).toEqual(['w']);
    expect(idsIn(el, '[aria-label="Pipeline in dispatch order"]')).toEqual(['p']);
    expect(idsIn(el, '[aria-label="Blocked issues"]')).toEqual(['b']);
  });

  it('labels each band by its heading', async () => {
    // Act
    const { el } = await loaded([item('w', 3)]);

    // Assert
    const sections = [...el.querySelectorAll('.board section')];
    expect(sections.map((s) => s.getAttribute('aria-labelledby'))).toEqual([
      'board-band-needs',
      'board-band-progress',
    ]);
  });

  describe('Needs you', () => {
    it('says nothing needs you, untinted, when it is empty', async () => {
      // Act
      const { el } = await loaded([item('w', 3)]);

      // Assert
      expect(text(el.querySelector('.needs-band__empty'))).toBe('Nothing needs you');
      expect(el.querySelector('.board-band--tinted')).toBeNull();
    });

    it('is the one tinted band, and counts its alerts and cards', async () => {
      // Act
      const { el } = await loaded([
        item('n', 4, { state: 'failed', needsAction: true }),
        item('q', 5, {
          state: 'queued',
          awaitsDispatch: true,
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
          repositoryEligibilityStatus: 'ineligible',
          repositoryId: 'repo-9',
        }),
        item('b', 5, {
          state: 'queued',
          awaitsDispatch: true,
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
          stage: 'awaiting_review',
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
      const card = el.querySelector('fd-needs-card')!;
      expect(text(card.querySelector('.needs-card__reason'))).toBe('Needs answers');
      expect(text(card.querySelector('fd-stage-chip'))).toBe('Stage: Implement');
      expect(text(card.querySelector('.needs-card__repo'))).toBe('acme/api #42');
      expect(text(card.querySelector('.needs-card__why'))).toBe(
        'Questions are waiting for your answers',
      );
      expect(text(card.querySelector('time'))).toBe('waiting 12m');
    });

    it('opens the pane from its one button and offers no state-changing control', async () => {
      // Act
      const { el } = await loaded([
        item('n', 42, { title: 'Add retry', state: 'failed', needsAction: true }),
      ]);

      // Assert
      const card = el.querySelector('fd-needs-card')!;
      const buttons = card.querySelectorAll('button');
      expect(buttons).toHaveLength(1);
      expect(buttons[0].getAttribute('aria-label')).toBe('Add retry, #42');
      expect(buttons[0].getAttribute('aria-haspopup')).toBe('dialog');
      expect(card.querySelector('a')).toBeNull();
    });

    it('links a ready pull request for review in a new tab', async () => {
      // Act
      const { el } = await loaded([
        item('r', 7, {
          stage: 'awaiting_review',
          state: 'review',
          needsAction: true,
          providerType: 'gitlab',
          pullRequestUrl: 'https://gitlab.com/acme/api/-/merge_requests/3',
        }),
      ]);

      // Assert
      const link = el.querySelector<HTMLAnchorElement>('fd-needs-card a')!;
      expect(link.getAttribute('href')).toBe('https://gitlab.com/acme/api/-/merge_requests/3');
      expect(link.getAttribute('target')).toBe('_blank');
      expect(link.getAttribute('rel')).toBe('noopener noreferrer');
      expect(text(link)).toBe('Review MR ↗ (opens GitLab in a new tab)');
    });
  });

  describe('In progress', () => {
    it('shows a worker card with its role, elapsed time and a link to its Floor lane', async () => {
      // Act
      const { el } = await loaded([
        item('w', 3, {
          stage: 'triage',
          state: 'triage_in_progress',
          enteredStateAt: minutesAgo(12),
        }),
      ]);

      // Assert
      const card = el.querySelector('fd-worker-card')!;
      expect(text(card.querySelector('fd-role-tag'))).toBe('Role: triage');
      expect(text(card.querySelector('.worker-card__elapsed'))).toBe('Running for running 12m');
      const link = card.querySelector('a')!;
      expect(text(link)).toBe('Watch on Floor');
      expect(link.getAttribute('href')).toBe('/floor#floor-lane-w');
    });

    it('lists the steps Foundry runs under their own heading', async () => {
      // Act
      const { el } = await loaded([item('s', 3, { state: 'checks_pending', codeStep: 'checks' })]);

      // Assert
      expect(text(el.querySelector('h3'))).toBe('Handled by Foundry');
      const row = el.querySelector('[aria-label="Steps Foundry is running"] fd-board-row')!;
      expect(text(row.querySelector('fd-step-chip'))).toBe('Checks running…');
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

    it('says how many are queued when nothing runs', async () => {
      // Act
      const { el } = await loaded([
        item('a', 1, { state: 'queued', awaitsDispatch: true }),
        item('b', 2, { state: 'queued', awaitsDispatch: true }),
      ]);

      // Assert
      expect(text(el.querySelector('.progress-band__empty'))).toBe(
        'No runs. 2 queued, waiting to dispatch',
      );
    });

    it('says idle when nothing runs or waits', async () => {
      // Act
      const { el } = await loaded([item('n', 4, { state: 'failed', needsAction: true })]);

      // Assert
      expect(text(el.querySelector('.progress-band__empty'))).toBe('Idle');
    });
  });

  describe('Pipeline', () => {
    it('lists every queued issue in global dispatch order with its position', async () => {
      // Act
      const { el } = await loaded([
        item('t', 1, { stage: 'triage', state: 'triage_queued', awaitsDispatch: true }),
        item('d', 2, { stage: 'triage', state: 'detected', awaitsDispatch: true }),
        item('x', 3, {
          state: 'queued',
          awaitsDispatch: true,
          repositoryEligibilityStatus: 'unreachable',
        }),
        item('i', 4, { state: 'queued', awaitsDispatch: true }),
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
          repositorySlug: 'acme/isal-monitoring',
        }),
      ]);

      // Assert
      const row = el.querySelector('fd-board-row')!;
      const button = row.querySelector('button')!;
      expect(button.getAttribute('aria-label')).toBe('Fix alerts, #32');
      const repo = row.querySelector('.board-row__repo')!;
      expect(repo.getAttribute('title')).toBe('acme/isal-monitoring');
      expect(button.getAttribute('aria-describedby')).toContain(repo.id);
      expect(text(repo.querySelector('[aria-hidden="true"]'))).toBe('isal-monitoring');
    });

    it('shows the first 10 rows until Show all', async () => {
      // Arrange
      const queued = Array.from({ length: 12 }, (_, i) =>
        item(`q${i}`, i + 1, { state: 'queued', awaitsDispatch: true }),
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
      const h = await loaded([item('i', 4, { state: 'queued', awaitsDispatch: true })]);

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

    it('is hidden with no rows', async () => {
      // Act
      const { el } = await loaded([item('w', 3)]);

      // Assert
      expect(el.querySelector('fd-pipeline-band')).toBeNull();
    });
  });

  describe('Blocked', () => {
    const blocked = () => [
      item('b', 1, { state: 'blocked', awaitsDispatch: true, waitsOnBlockers: true }),
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

  it('keeps its last state, marked stale, while the connection is lost', async () => {
    // Arrange
    const h = await loaded([item('f', 2)]);

    // Act
    h.status.set('reconnecting');
    await h.render();

    // Assert
    expect(h.el.querySelector('.board')?.classList).toContain('board--stale');
    expect(h.el.textContent).toContain('Connection lost — showing last known state');
    expect(idsIn(h.el, '.board')).toEqual(['f']);
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
        item('f', 142, { state: 'blocked', awaitsDispatch: true, waitsOnBlockers: true }) as never,
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
