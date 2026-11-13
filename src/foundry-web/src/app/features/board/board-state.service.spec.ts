import { TestBed } from '@angular/core/testing';
import { WritableSignal, computed, signal } from '@angular/core';
import { vi } from 'vitest';
import { IssueSummary } from '../issues/issue.model';
import { IssueService } from '../issues/issue.service';
import { ConnectionStatus, IssueSignalRService } from '../../core/services/issue-signalr.service';
import { BoardColumnView, BoardStateService } from './board-state.service';

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
}

function setup(): Harness {
  const issues = signal<IssueSummary[]>([]);
  const initialLoading = signal(true);
  const status = signal<ConnectionStatus>('connected');
  const hubHandlers = new Map<string, (data: IssueSummary) => void>();
  const issueService = {
    issues,
    initialLoading,
    eligibleQueuedIssues: computed(() => issues().filter((i) => i.awaitsDispatch && i.state !== 'blocked')),
  };
  TestBed.configureTestingModule({
    providers: [
      BoardStateService,
      { provide: IssueService, useValue: issueService },
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
  return { service: TestBed.inject(BoardStateService), issues, initialLoading, status, hubHandlers };
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

function idsIn(columns: readonly BoardColumnView[], cell: 'queue' | 'flow' | 'needs'): string[] {
  return columns.flatMap((c) => c[cell].map((card) => card.issue.id));
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
  return { ready: Promise.resolve(), updateCallbackDone: Promise.resolve(), finished: Promise.resolve() };
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
    expect(idsIn(h.service.columns(), 'flow')).toEqual(['a']);
  });

  it('renders nothing while the first load is pending', () => {
    // Arrange
    const h = setup();

    // Act
    h.issues.set([buildIssue({ id: 'a' })]);
    TestBed.tick();

    // Assert
    expect(idsIn(h.service.columns(), 'flow')).toEqual([]);
  });

  it('holds a later change until the batching window closes', () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a' })]);

    // Act
    change(h, [buildIssue({ id: 'a', needsAction: true, state: 'failed' })]);
    vi.advanceTimersByTime(149);

    // Assert
    expect(idsIn(h.service.columns(), 'needs')).toEqual([]);
  });

  it('applies a change 150 ms after the last one', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a' })]);

    // Act
    change(h, [buildIssue({ id: 'a', needsAction: true, state: 'failed' })]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(idsIn(h.service.columns(), 'needs')).toEqual(['a']);
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
    expect(h.service.columns()[1].flow[0].issue.title).toBe('t4');
  });

  it('runs the placement changes of one window as one view transition', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a' }), buildIssue({ id: 'b' })]);
    const start = stubViewTransition();

    // Act
    change(h, [buildIssue({ id: 'a', needsAction: true, state: 'failed' }), buildIssue({ id: 'b' })]);
    change(h, [
      buildIssue({ id: 'a', needsAction: true, state: 'failed' }),
      buildIssue({ id: 'b', stage: 'awaiting_review', state: 'review', needsAction: true }),
    ]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(start).toHaveBeenCalledTimes(1);
    expect(idsIn(h.service.columns(), 'needs')).toEqual(['a', 'b']);
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
    expect(h.service.columns()[1].flow[0].issue.title).toBe('Renamed');
  });

  it('applies changes without animation when the browser has no view transitions', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a' })]);

    // Act
    change(h, [buildIssue({ id: 'a', stage: 'awaiting_review', state: 'review', needsAction: true })]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(idsIn(h.service.columns(), 'needs')).toEqual(['a']);
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
    expect(idsIn(h.service.columns(), 'needs')).toEqual(['a']);
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
    expect(h.service.columns()[1].needs[0].arrived).toBe(true);
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
    expect(h.service.columns()[1].needs[0].arrived).toBe(false);
  });

  it('does not mark cards that already needed a person on the first load', () => {
    // Arrange
    const h = setup();

    // Act
    load(h, [buildIssue({ id: 'a', needsAction: true, state: 'failed' })]);

    // Assert
    expect(h.service.columns()[1].needs[0].arrived).toBe(false);
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
    change(h, [buildIssue({ id: 'a', issueNumber: 60, state: 'rebase_pending', codeStep: 'rebasing' })]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(h.service.announcement()).toBe('#60 Rebasing…');
  });

  it('does not announce a code step that has not changed', async () => {
    // Arrange
    const h = setup();
    load(h, [buildIssue({ id: 'a', state: 'checks_pending', codeStep: 'checks' })]);

    // Act
    change(h, [buildIssue({ id: 'a', state: 'checks_pending', codeStep: 'checks', title: 'Renamed' })]);
    await vi.advanceTimersByTimeAsync(150);

    // Assert
    expect(h.service.announcement()).toBe('');
  });

  describe('completion', () => {
    function complete(h: Harness, issue: IssueSummary): void {
      const completed = { ...issue, state: 'completed' as const, stage: 'done' as const, needsAction: false };
      h.hubHandlers.get('IssueUpdated')?.(completed);
      change(h, h.issues().filter((i) => i.id !== issue.id));
    }

    it('keeps a confirming ghost of a completed card where it was', async () => {
      // Arrange
      const h = setup();
      const review = buildIssue({ id: 'a', stage: 'awaiting_review', state: 'review', needsAction: true });
      load(h, [buildIssue({ id: 'z', stage: 'awaiting_review', state: 'review', needsAction: true }), review]);

      // Act
      complete(h, review);
      await vi.advanceTimersByTimeAsync(150);

      // Assert
      const needs = h.service.columns()[2].needs;
      expect(needs.map((c) => [c.issue.id, c.phase])).toEqual([
        ['z', 'live'],
        ['a', 'confirm'],
      ]);
    });

    it('collapses the ghost after 1200 ms', async () => {
      // Arrange
      const h = setup();
      const review = buildIssue({ id: 'a', stage: 'awaiting_review', state: 'review', needsAction: true });
      load(h, [review]);
      complete(h, review);

      // Act
      await vi.advanceTimersByTimeAsync(1200);

      // Assert
      expect(h.service.columns()[2].needs[0].phase).toBe('collapse');
    });

    it('removes the card once the collapse has run', async () => {
      // Arrange
      const h = setup();
      const review = buildIssue({ id: 'a', stage: 'awaiting_review', state: 'review', needsAction: true });
      load(h, [review]);
      complete(h, review);

      // Act
      await vi.advanceTimersByTimeAsync(1200 + 180);

      // Assert
      expect(h.service.columns()[2].needs).toEqual([]);
    });

    it('announces the completion', async () => {
      // Arrange
      const h = setup();
      const review = buildIssue({ id: 'a', issueNumber: 139, stage: 'awaiting_review', state: 'review', needsAction: true });
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
      expect(idsIn(h.service.columns(), 'flow')).toEqual([]);
    });
  });

  it('leaves out a card with an unknown stage and warns', () => {
    // Arrange
    const h = setup();
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});

    // Act
    load(h, [buildIssue({ id: 'a', stage: null })]);

    // Assert
    expect(idsIn(h.service.columns(), 'flow')).toEqual([]);
    expect(warn).toHaveBeenCalled();
  });

  it('counts the cards waiting in the queues', () => {
    // Arrange
    const h = setup();

    // Act
    load(h, [
      buildIssue({ id: 'a', awaitsDispatch: true, state: 'queued' }),
      buildIssue({ id: 'b', awaitsDispatch: true, state: 'triage_queued', stage: 'triage' }),
      buildIssue({ id: 'c' }),
    ]);

    // Assert
    expect(h.service.waitingCount()).toBe(2);
  });

  it('counts the cards that need a person', () => {
    // Arrange
    const h = setup();

    // Act
    load(h, [buildIssue({ id: 'a', needsAction: true, state: 'failed' }), buildIssue({ id: 'b' })]);

    // Assert
    expect(h.service.needsCount()).toBe(1);
  });

  it('stops listening to the hub when destroyed', () => {
    // Arrange
    const h = setup();

    // Act
    TestBed.resetTestingModule();

    // Assert
    expect(h.hubHandlers.has('IssueUpdated')).toBe(false);
  });
});
