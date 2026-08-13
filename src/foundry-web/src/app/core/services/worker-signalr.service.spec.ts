import { TestBed } from '@angular/core/testing';
import { firstValueFrom, take, toArray } from 'rxjs';
import { WorkerSignalRService, WORKER_HUB_FACTORY, WorkerHub } from './worker-signalr.service';
import { WorkerActivity } from '../models/worker-activity.model';

interface CapturedHubCallbacks {
  onWorkerActivity: ((activity: WorkerActivity) => void) | null;
  onReconnected: (() => void) | null;
}

function buildMockHub(captured: CapturedHubCallbacks): WorkerHub {
  return {
    on: (_method: string, cb: (activity: WorkerActivity) => void) => {
      captured.onWorkerActivity = cb;
    },
    onReconnected: (cb: () => void) => {
      captured.onReconnected = cb;
    },
    stream: (_method: string, _workerRunId: string) => ({
      subscribe: (_callbacks: unknown) => ({ dispose: () => { /* no-op */ } }),
    }),
    start: () => Promise.resolve(),
  };
}

function setup() {
  TestBed.resetTestingModule();

  const captured: CapturedHubCallbacks = { onWorkerActivity: null, onReconnected: null };
  const mockHubFactory = () => buildMockHub(captured);

  TestBed.configureTestingModule({
    providers: [
      WorkerSignalRService,
      { provide: WORKER_HUB_FACTORY, useValue: mockHubFactory },
    ],
  });

  return {
    svc: TestBed.inject(WorkerSignalRService),
    captured,
  };
}

describe('WorkerSignalRService', () => {
  // Cycle 1: service constructs without error
  it('should create the service', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect(svc).toBeTruthy();
  });

  // Cycle 2: WorkerActivity signal updates when hub broadcasts
  it('should update workerActivity signal when WorkerActivity event is received', () => {
    // Arrange
    const { svc, captured } = setup();
    const activity: WorkerActivity = {
      workerRunId: 'run-1',
      issueId: 'issue-1',
      lastActivityAt: '2026-01-01T00:00:00Z',
      commitCount: 3,
    };

    // Act
    captured.onWorkerActivity!(activity);

    // Assert
    expect(svc.workerActivity()).toEqual(activity);
  });

  // Cycle 3: workerActivity starts null
  it('should initialize workerActivity signal to null', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect(svc.workerActivity()).toBeNull();
  });

  // Cycle 4: second WorkerActivity replaces first
  it('should replace workerActivity signal when a new WorkerActivity arrives', () => {
    // Arrange
    const { svc, captured } = setup();
    const first: WorkerActivity = { workerRunId: 'run-1', issueId: 'issue-1', lastActivityAt: '2026-01-01T00:00:00Z', commitCount: 0 };
    const second: WorkerActivity = { workerRunId: 'run-1', issueId: 'issue-1', lastActivityAt: '2026-01-01T00:01:00Z', commitCount: 1 };
    captured.onWorkerActivity!(first);

    // Act
    captured.onWorkerActivity!(second);

    // Assert
    expect(svc.workerActivity()).toEqual(second);
  });

  // Cycle 5: reconnected emits when hub reconnects
  it('should emit on reconnected when hub reconnects', () => {
    // Arrange
    const { svc, captured } = setup();
    let count = 0;
    svc.reconnected.subscribe(() => count++);

    // Act
    captured.onReconnected!();

    // Assert
    expect(count).toBe(1);
  });

  // Cycle 7: activityFor returns null for unknown workerRunId
  it('should return null from activityFor for an unknown workerRunId', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect(svc.activityFor('unknown-run')).toBeNull();
  });

  it('should return lastActivityAt from activityFor after WorkerActivity is received', () => {
    // Arrange
    const { svc, captured } = setup();
    const activity: WorkerActivity = {
      workerRunId: 'run-A',
      issueId: 'issue-A',
      lastActivityAt: '2026-06-01T12:00:00Z',
      commitCount: 2,
    };

    // Act
    captured.onWorkerActivity!(activity);

    // Assert
    expect(svc.activityFor('run-A')).toBe('2026-06-01T12:00:00Z');
  });

  it('should return null from activityForIssue for an unknown issueId', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect(svc.activityForIssue('unknown-issue')).toBeNull();
  });

  it('should return lastActivityAt from activityForIssue after WorkerActivity is received', () => {
    // Arrange
    const { svc, captured } = setup();
    const activity: WorkerActivity = {
      workerRunId: 'run-B',
      issueId: 'issue-B',
      lastActivityAt: '2026-06-01T13:00:00Z',
      commitCount: 5,
    };

    // Act
    captured.onWorkerActivity!(activity);

    // Assert
    expect(svc.activityForIssue('issue-B')).toBe('2026-06-01T13:00:00Z');
    // The other issue is still null
    expect(svc.activityForIssue('issue-A')).toBeNull();
  });

  it('should independently track activity for two different issues', () => {
    // Arrange
    const { svc, captured } = setup();
    const activityA: WorkerActivity = {
      workerRunId: 'run-A',
      issueId: 'issue-A',
      lastActivityAt: '2026-06-01T12:00:00Z',
      commitCount: 1,
    };
    const activityB: WorkerActivity = {
      workerRunId: 'run-B',
      issueId: 'issue-B',
      lastActivityAt: '2026-06-01T14:00:00Z',
      commitCount: 3,
    };

    // Act
    captured.onWorkerActivity!(activityA);
    captured.onWorkerActivity!(activityB);

    // Assert — each issue returns its own lastActivityAt
    expect(svc.activityForIssue('issue-A')).toBe('2026-06-01T12:00:00Z');
    expect(svc.activityForIssue('issue-B')).toBe('2026-06-01T14:00:00Z');
  });

  // Cycle 6: reconnected is an Observable (not a writable Subject)
  it('should expose reconnected as an Observable (no next() method)', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect((svc.reconnected as unknown as { next?: unknown }).next).toBeUndefined();
  });

  // Cycle 8: commitCount is parsed from an incoming WorkerActivity
  it('should return 0 from commitCountForIssue for an unknown issueId', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect(svc.commitCountForIssue('unknown-issue')).toBe(0);
  });

  it('should return commitCount from commitCountForIssue after WorkerActivity is received', () => {
    // Arrange
    const { svc, captured } = setup();
    const activity: WorkerActivity = {
      workerRunId: 'run-C',
      issueId: 'issue-C',
      lastActivityAt: '2026-06-01T15:00:00Z',
      commitCount: 7,
    };

    // Act
    captured.onWorkerActivity!(activity);

    // Assert
    expect(svc.commitCountForIssue('issue-C')).toBe(7);
  });

  it('should update commitCount when a new WorkerActivity replaces the previous one for the same issue', () => {
    // Arrange
    const { svc, captured } = setup();
    const first: WorkerActivity = {
      workerRunId: 'run-D',
      issueId: 'issue-D',
      lastActivityAt: '2026-06-01T16:00:00Z',
      commitCount: 2,
    };
    const second: WorkerActivity = {
      workerRunId: 'run-D',
      issueId: 'issue-D',
      lastActivityAt: '2026-06-01T16:01:00Z',
      commitCount: 4,
    };
    captured.onWorkerActivity!(first);

    // Act
    captured.onWorkerActivity!(second);

    // Assert
    expect(svc.commitCountForIssue('issue-D')).toBe(4);
  });

  it('should independently track commitCount for two different issues', () => {
    // Arrange
    const { svc, captured } = setup();
    const activityE: WorkerActivity = {
      workerRunId: 'run-E',
      issueId: 'issue-E',
      lastActivityAt: '2026-06-01T17:00:00Z',
      commitCount: 1,
    };
    const activityF: WorkerActivity = {
      workerRunId: 'run-F',
      issueId: 'issue-F',
      lastActivityAt: '2026-06-01T18:00:00Z',
      commitCount: 9,
    };

    // Act
    captured.onWorkerActivity!(activityE);
    captured.onWorkerActivity!(activityF);

    // Assert
    expect(svc.commitCountForIssue('issue-E')).toBe(1);
    expect(svc.commitCountForIssue('issue-F')).toBe(9);
  });
});
