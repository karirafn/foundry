import { TestBed } from '@angular/core/testing';
import { WritableSignal, computed, signal } from '@angular/core';
import { vi } from 'vitest';
import { IssueSummary } from '../issues/issue.model';
import { IssueService } from '../issues/issue.service';
import { ConnectionStatus, IssueSignalRService } from '../../core/services/issue-signalr.service';
import { WorkerSignalRService } from '../../core/services/worker-signalr.service';
import { AgentActivity } from '../../core/models/agent-activity.model';
import { RepositorySummary } from '../settings/repositories/repository.model';
import { RepositoryService } from '../settings/repositories/repository.service';
import { BoardBandsView, BoardStateService } from './board-state.service';

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

interface FakeTransition {
  ready: Promise<void>;
  updateCallbackDone: Promise<void>;
  finished: Promise<void>;
}

interface Harness {
  service: BoardStateService;
  issues: WritableSignal<IssueSummary[]>;
  initialLoading: WritableSignal<boolean>;
  status: WritableSignal<ConnectionStatus>;
  hubHandlers: Map<string, (data: IssueSummary) => void>;
  repositories: WritableSignal<RepositorySummary[]>;
  activities: WritableSignal<ReadonlyMap<string, AgentActivity>>;
}

function buildActivity(overrides: Partial<AgentActivity> = {}): AgentActivity {
  return {
    workerRunId: 'run-1',
    issueId: 'issue-1',
    role: 'implement',
    lastEventAt: null,
    tasks: [],
    currentTask: null,
    completedTaskCount: 0,
    subagents: [],
    activeSkill: null,
    inFlightTool: null,
    tokens: { inputTokens: 0, outputTokens: 0 },
    ...overrides,
  };
}

function setup(): Harness {
  const issues = signal<IssueSummary[]>([]);
  const activities = signal<ReadonlyMap<string, AgentActivity>>(new Map());
  const initialLoading = signal(true);
  const status = signal<ConnectionStatus>('connected');
  const hubHandlers = new Map<string, (data: IssueSummary) => void>();
  const repositories = signal<RepositorySummary[]>([]);
  const issueService = {
    issues,
    initialLoading,
    eligibleQueuedIssues: computed(() =>
      issues().filter((i) => i.awaitsDispatch && i.state !== 'blocked'),
    ),
  };
  TestBed.configureTestingModule({
    providers: [
      BoardStateService,
      { provide: IssueService, useValue: issueService },
      { provide: RepositoryService, useValue: { repositories } },
      {
        provide: WorkerSignalRService,
        useValue: { agentActivityForIssue: (id: string) => activities().get(id) ?? null },
      },
      {
        provide: IssueSignalRService,
        useValue: {
          connectionStatus: status,
          on: (method: string, cb: (data: IssueSummary) => void) => hubHandlers.set(method, cb),
          off: (method: string) => hubHandlers.delete(method),
        },
      },
    ],
  });
  return {
    service: TestBed.inject(BoardStateService),
    issues,
    initialLoading,
    status,
    hubHandlers,
    repositories,
    activities,
  };
}

function load(h: Harness, issues: IssueSummary[]): void {
  h.issues.set(issues);
  h.initialLoading.set(false);
  TestBed.tick();
}

function change(h: Harness, issues: IssueSummary[]): void {
  h.issues.set(issues);
  TestBed.tick();
}

function idsIn(
  bands: BoardBandsView,
  band: 'needs' | 'workers' | 'steps' | 'pipeline' | 'blocked',
): string[] {
  return bands[band].map((item) => item.issue.id);
}

function stubViewTransition(make: () => FakeTransition = resolvedTransition) {
  const start = vi.fn((update: () => Promise<void> | void) => {
    void update();
    return make();
  });
  (document as unknown as { startViewTransition?: unknown }).startViewTransition = start;
  return start;
}

function resolvedTransition(): FakeTransition {
  return {
    ready: Promise.resolve(),
    updateCallbackDone: Promise.resolve(),
    finished: Promise.resolve(),
  };
}

describe('BoardStateService', () => {
  beforeEach(() => vi.useFakeTimers());

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    delete (document as unknown as { startViewTransition?: unknown }).startViewTransition;
  });

  it('renders the first load straight away', () => {
    // Arrange
    const h = setup();

    // Act
    load(h, [buildIssue({ id: 'a' })]);

    // Assert
    expect(idsIn(h.service.bands(), 'workers')).toEqual(['a']);
  });

  it('renders nothing while the first load is pending', () => {
    // Arrange
    const h = setup();

    // Act
    h.issues.set([buildIssue({ id: 'a' })]);
    TestBed.tick();

    // Assert
    expect(idsIn(h.service.bands(), 'workers')).toEqual([]);
  });

  it('holds a later change until the batching window closes', () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a' })]);

    // Act
    change(h, [buildIssue({ id: 'a', needsAction: true, state: 'failed' })]);
    vi.advanceTimersByTime(149);

    // Assert
    expect(idsIn(h.service.bands(), 'needs')).toEqual([]);
  });

  it('applies a change 150 ms after the last one', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a' })]);

    // Act
    change(h, [buildIssue({ id: 'a', needsAction: true, state: 'failed' })]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(idsIn(h.service.bands(), 'needs')).toEqual(['a']);
  });

  it('applies a burst of changes no later than 500 ms after the first', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a' })]);

    // Act
    for (let i = 0; i < 5; i++) {
      change(h, [buildIssue({ id: 'a', title: `t${i}` })]);
      await vi.advanceTimersByTimeAsync(100);
    }

    // Assert
    expect(h.service.bands().workers[0].issue.title).toBe('t4');
  });

  it('runs the placement changes of one window as one view transition', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a' }), buildIssue({ id: 'b' })]);
    const start = stubViewTransition();

    // Act
    change(h, [
      buildIssue({ id: 'a', needsAction: true, state: 'failed' }),
      buildIssue({ id: 'b' }),
    ]);
    change(h, [
      buildIssue({ id: 'a', needsAction: true, state: 'failed' }),
      buildIssue({ id: 'b', stage: 'awaiting_review', state: 'review', needsAction: true }),
    ]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(start).toHaveBeenCalledTimes(1);
    expect(idsIn(h.service.bands(), 'needs')).toEqual(['a', 'b']);
  });

  it('applies a change that moves no card without a view transition', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a' })]);
    const start = stubViewTransition();

    // Act
    change(h, [buildIssue({ id: 'a', title: 'Renamed' })]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(start).not.toHaveBeenCalled();
    expect(h.service.bands().workers[0].issue.title).toBe('Renamed');
  });

  it('applies changes without animation when the browser has no view transitions', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a' })]);

    // Act
    change(h, [
      buildIssue({ id: 'a', stage: 'awaiting_review', state: 'review', needsAction: true }),
    ]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(idsIn(h.service.bands(), 'needs')).toEqual(['a']);
  });

  it('catches the AbortError of a skipped transition', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a' })]);
    const abort = new DOMException('Skipped', 'AbortError');
    stubViewTransition(() => ({
      ready: Promise.reject(abort),
      updateCallbackDone: Promise.reject(abort),
      finished: Promise.reject(abort),
    }));
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});

    // Act
    change(h, [buildIssue({ id: 'a', needsAction: true, state: 'failed' })]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(error).not.toHaveBeenCalled();
    expect(idsIn(h.service.bands(), 'needs')).toEqual(['a']);
  });

  it('snaps without a transition while the connection is lost', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a' })]);
    const start = stubViewTransition();
    h.status.set('reconnecting');
    TestBed.tick();

    // Act
    change(h, [buildIssue({ id: 'a', needsAction: true, state: 'failed' })]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(start).not.toHaveBeenCalled();
  });

  it('snaps the first batch after a reconnect', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a' })]);
    const start = stubViewTransition();
    h.status.set('reconnecting');
    TestBed.tick();
    h.status.set('connected');
    TestBed.tick();

    // Act
    change(h, [buildIssue({ id: 'a', needsAction: true, state: 'failed' })]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(start).not.toHaveBeenCalled();
  });

  it('reports the connection as stale while it is not connected', () => {
    // Arrange
    const h = setup();

    // Act
    h.status.set('disconnected');

    // Assert
    expect(h.service.stale()).toBe(true);
  });

  it('marks a card that starts needing a person as arrived', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a' })]);

    // Act
    change(h, [buildIssue({ id: 'a', needsAction: true, state: 'failed' })]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(h.service.bands().needs[0].arrived).toBe(true);
  });

  it('clears the arrival after 1300 ms', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a' })]);
    change(h, [buildIssue({ id: 'a', needsAction: true, state: 'failed' })]);
    await vi.advanceTimersByTimeAsync(150);

    // Act
    await vi.advanceTimersByTimeAsync(1300);

    // Assert
    expect(h.service.bands().needs[0].arrived).toBe(false);
  });

  it('does not mark cards that already needed a person on the first load', () => {
    // Arrange
    const h = setup();

    // Act
    load(h, [buildIssue({ id: 'a', needsAction: true, state: 'failed' })]);

    // Assert
    expect(h.service.bands().needs[0].arrived).toBe(false);
  });

  it('announces a card that starts needing a person', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a', issueNumber: 142 })]);

    // Act
    change(h, [buildIssue({ id: 'a', issueNumber: 142, needsAction: true, state: 'failed' })]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(h.service.announcement()).toBe('#142 needs you: Failed');
  });

  it('announces a card that starts a code step', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a', issueNumber: 60 })]);

    // Act
    change(h, [
      buildIssue({ id: 'a', issueNumber: 60, state: 'rebase_pending', codeStep: 'rebasing' }),
    ]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(h.service.announcement()).toBe('#60 Rebasing…');
  });

  it('does not announce a code step that has not changed', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a', state: 'checks_pending', codeStep: 'checks' })]);

    // Act
    change(h, [
      buildIssue({ id: 'a', state: 'checks_pending', codeStep: 'checks', title: 'Renamed' }),
    ]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(h.service.announcement()).toBe('');
  });

  describe('completion', () => {
    function complete(h: Harness, issue: IssueSummary): void {
      const completed = {
        ...issue,
        state: 'completed' as const,
        stage: 'done' as const,
        needsAction: false,
      };
      h.hubHandlers.get('IssueUpdated')?.(completed);
      change(
        h,
        h.issues().filter((i) => i.id !== issue.id),
      );
    }

    it('keeps a confirming ghost of a completed card where it was', async () => {
      // Arrange
      const h = setup();
      const review = buildIssue({
        id: 'a',
        stage: 'awaiting_review',
        state: 'review',
        needsAction: true,
      });
      load(h, [
        buildIssue({ id: 'z', stage: 'awaiting_review', state: 'review', needsAction: true }),
        review,
      ]);

      // Act
      complete(h, review);
      await vi.advanceTimersByTimeAsync(150);

      // Assert
      const needs = h.service.bands().needs;
      expect(needs.map((c) => [c.issue.id, c.phase])).toEqual([
        ['z', 'live'],
        ['a', 'confirm'],
      ]);
    });

    it('collapses the ghost after 1200 ms', async () => {
      // Arrange
      const h = setup();
      const review = buildIssue({
        id: 'a',
        stage: 'awaiting_review',
        state: 'review',
        needsAction: true,
      });
      load(h, [review]);
      complete(h, review);

      // Act
      await vi.advanceTimersByTimeAsync(1200);

      // Assert
      expect(h.service.bands().needs[0].phase).toBe('collapse');
    });

    it('removes the card once the collapse has run', async () => {
      // Arrange
      const h = setup();
      const review = buildIssue({
        id: 'a',
        stage: 'awaiting_review',
        state: 'review',
        needsAction: true,
      });
      load(h, [review]);
      complete(h, review);

      // Act
      await vi.advanceTimersByTimeAsync(1200 + 180);

      // Assert
      expect(h.service.bands().needs).toEqual([]);
    });

    it('announces the completion', async () => {
      // Arrange
      const h = setup();
      const review = buildIssue({
        id: 'a',
        issueNumber: 139,
        stage: 'awaiting_review',
        state: 'review',
        needsAction: true,
      });
      load(h, [review]);

      // Act
      complete(h, review);
      await vi.advanceTimersByTimeAsync(150);

      // Assert
      expect(h.service.announcement()).toBe('#139 completed');
    });

    it('lets a card that leaves for another reason go without a ghost', async () => {
      // Arrange
      const h = setup();
      load(h, [buildIssue({ id: 'a' })]);

      // Act
      change(h, []);
      await vi.advanceTimersByTimeAsync(150);

      // Assert
      expect(idsIn(h.service.bands(), 'workers')).toEqual([]);
    });
  });

  it('leaves out a card with an unknown stage and warns', () => {
    // Arrange
    const h = setup();
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});

    // Act
    load(h, [buildIssue({ id: 'a', stage: null })]);

    // Assert
    expect(idsIn(h.service.bands(), 'workers')).toEqual([]);
    expect(warn).toHaveBeenCalled();
  });

  it('counts every alert and item on the board', () => {
    // Arrange
    const h = setup();

    // Act
    load(h, [
      buildIssue({
        id: 'a',
        awaitsDispatch: true,
        state: 'queued',
        repositoryEligibilityStatus: 'ineligible',
      }),
      buildIssue({ id: 'b', needsAction: true, state: 'failed' }),
      buildIssue({ id: 'c' }),
    ]);

    // Assert
    expect(h.service.itemCount()).toBe(4);
  });

  it('places the issues in their bands', () => {
    // Arrange
    const h = setup();

    // Act
    load(h, [
      buildIssue({ id: 'n', needsAction: true, state: 'failed' }),
      buildIssue({ id: 'w' }),
      buildIssue({ id: 's', state: 'checks_pending', codeStep: 'checks' }),
      buildIssue({ id: 'p', state: 'queued', awaitsDispatch: true }),
      buildIssue({ id: 'b', state: 'blocked', awaitsDispatch: true, waitsOnBlockers: true }),
    ]);

    // Assert
    const bands = h.service.bands();
    expect([
      idsIn(bands, 'needs'),
      idsIn(bands, 'workers'),
      idsIn(bands, 'steps'),
      idsIn(bands, 'pipeline'),
      idsIn(bands, 'blocked'),
    ]).toEqual([['n'], ['w'], ['s'], ['p'], ['b']]);
  });

  it('applies a pipeline renumbering without a view transition', async () => {
    // Arrange
    const h = setup();
    const a = buildIssue({ id: 'a', state: 'queued', awaitsDispatch: true });
    const b = buildIssue({ id: 'b', state: 'queued', awaitsDispatch: true });
    load(h, [a, b]);
    const start = stubViewTransition();

    // Act
    change(h, [b, a]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(start).not.toHaveBeenCalled();
    expect(idsIn(h.service.bands(), 'pipeline')).toEqual(['b', 'a']);
  });

  it('glides a band change even while the pipeline renumbers', async () => {
    // Arrange
    const h = setup();
    const queued = Array.from({ length: 20 }, (_, i) =>
      buildIssue({ id: `q${i}`, state: 'queued', awaitsDispatch: true }),
    );
    load(h, [buildIssue({ id: 'w' }), ...queued]);
    const start = stubViewTransition();

    // Act
    change(h, [buildIssue({ id: 'w', needsAction: true, state: 'failed' }), ...queued.reverse()]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(start).toHaveBeenCalledTimes(1);
  });

  it('snaps when more than 12 cards change band', async () => {
    // Arrange
    const h = setup();
    const running = Array.from({ length: 13 }, (_, i) => buildIssue({ id: `w${i}` }));
    load(h, running);
    const start = stubViewTransition();

    // Act
    change(
      h,
      running.map((i) => ({ ...i, needsAction: true, state: 'failed' as const })),
    );
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(start).not.toHaveBeenCalled();
    expect(h.service.bands().needs).toHaveLength(13);
  });

  it('announces a worker that starts', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a', issueNumber: 7, state: 'queued', awaitsDispatch: true })]);

    // Act
    change(h, [buildIssue({ id: 'a', issueNumber: 7, state: 'in_progress' })]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(h.service.announcement()).toBe('#7 started: implement');
  });

  it('announces a repository that newly holds issues', async () => {
    // Arrange
    const h = setup();
    load(h, [
      buildIssue({ id: 'a', state: 'queued', awaitsDispatch: true, repositorySlug: 'acme/web' }),
    ]);

    // Act
    change(h, [
      buildIssue({
        id: 'a',
        state: 'queued',
        awaitsDispatch: true,
        repositorySlug: 'acme/web',
        repositoryEligibilityStatus: 'ineligible',
      }),
    ]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(h.service.announcement()).toBe("web can't be worked on");
  });

  it('fills an alert from the repository list once it loads', async () => {
    // Arrange
    const h = setup();
    load(h, [
      buildIssue({
        id: 'a',
        state: 'queued',
        awaitsDispatch: true,
        repositoryEligibilityStatus: 'ineligible',
      }),
    ]);

    // Act
    h.repositories.set([
      {
        id: 'repo-1',
        slug: 'acme/api',
        eligibility: {
          status: 'ineligible',
          violations: [{ rule: 'r', description: 'Require pull requests' }],
          reason: null,
        },
      } as RepositorySummary,
    ]);
    TestBed.tick();
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(h.service.bands().alerts[0].firstViolation).toBe('Require pull requests');
  });

  it('stops listening to the hub when destroyed', () => {
    // Arrange
    const h = setup();

    // Act
    TestBed.resetTestingModule();

    // Assert
    expect(h.hubHandlers.has('IssueUpdated')).toBe(false);
  });

  describe('clock and silence', () => {
    const START = Date.parse('2026-01-01T12:00:00Z');

    function silentRun(
      h: Harness,
      issueId: string,
      workerRunId: string,
      lastEventAt: string,
    ): void {
      h.activities.update((current) =>
        new Map(current).set(issueId, buildActivity({ issueId, workerRunId, lastEventAt })),
      );
      TestBed.tick();
    }

    beforeEach(() => vi.setSystemTime(START));

    it('ticks the shared clock every second', async () => {
      // Arrange
      const h = setup();

      // Act
      await vi.advanceTimersByTimeAsync(1000);

      // Assert
      expect(h.service.now()).toBe(START + 1000);
    });

    it('announces a worker that goes silent', async () => {
      // Arrange
      const h = setup();
      load(h, [buildIssue({ id: 'a', issueNumber: 7 })]);
      silentRun(h, 'a', 'run-a', new Date(START - 299_000).toISOString());

      // Act
      await vi.advanceTimersByTimeAsync(1000);
      TestBed.tick();

      // Assert
      expect(h.service.announcement()).toBe('#7 worker silent for 5 minutes');
    });

    it('does not announce a worker under the silence threshold', async () => {
      // Arrange
      const h = setup();
      load(h, [buildIssue({ id: 'a', issueNumber: 7 })]);
      silentRun(h, 'a', 'run-a', new Date(START - 60_000).toISOString());

      // Act
      await vi.advanceTimersByTimeAsync(1000);
      TestBed.tick();

      // Assert
      expect(h.service.announcement()).toBe('');
    });

    it('announces a silence episode once', async () => {
      // Arrange
      const h = setup();
      load(h, [
        buildIssue({ id: 'a', issueNumber: 7 }),
        buildIssue({ id: 'b', issueNumber: 8, state: 'queued', awaitsDispatch: true }),
      ]);
      silentRun(h, 'a', 'run-a', new Date(START - 300_000).toISOString());
      expect(h.service.announcement()).toBe('#7 worker silent for 5 minutes');
      change(h, [
        buildIssue({ id: 'a', issueNumber: 7 }),
        buildIssue({ id: 'b', issueNumber: 8, state: 'in_progress' }),
      ]);
      await vi.advanceTimersByTimeAsync(150);
      expect(h.service.announcement()).toBe('#8 started: implement');

      // Act
      await vi.advanceTimersByTimeAsync(5000);
      TestBed.tick();

      // Assert
      expect(h.service.announcement()).toBe('#8 started: implement');
    });

    it('announces a new silence episode after the run reports again', async () => {
      // Arrange
      const h = setup();
      load(h, [
        buildIssue({ id: 'a', issueNumber: 7 }),
        buildIssue({ id: 'b', issueNumber: 8, state: 'queued', awaitsDispatch: true }),
      ]);
      silentRun(h, 'a', 'run-a', new Date(START - 300_000).toISOString());
      change(h, [
        buildIssue({ id: 'a', issueNumber: 7 }),
        buildIssue({ id: 'b', issueNumber: 8, state: 'in_progress' }),
      ]);
      await vi.advanceTimersByTimeAsync(150);
      expect(h.service.announcement()).toBe('#8 started: implement');
      silentRun(h, 'a', 'run-a', new Date(START).toISOString());

      // Act
      await vi.advanceTimersByTimeAsync(300_000);
      TestBed.tick();

      // Assert
      expect(h.service.announcement()).toBe('#7 worker silent for 5 minutes');
    });

    it('does not announce silence for a run whose issue is not running', async () => {
      // Arrange
      const h = setup();
      load(h, [buildIssue({ id: 'a', issueNumber: 7, state: 'queued', awaitsDispatch: true })]);
      silentRun(h, 'a', 'run-a', new Date(START - 600_000).toISOString());

      // Act
      await vi.advanceTimersByTimeAsync(1000);
      TestBed.tick();

      // Assert
      expect(h.service.announcement()).toBe('');
    });
  });
});
