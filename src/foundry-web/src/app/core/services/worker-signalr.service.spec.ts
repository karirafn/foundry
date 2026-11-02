import { TestBed } from '@angular/core/testing';
import { firstValueFrom, take, toArray } from 'rxjs';
import { WorkerSignalRService, WORKER_HUB_FACTORY, WorkerHub } from './worker-signalr.service';
import { WorkerActivity } from '../models/worker-activity.model';
import { AgentActivity, AgentActivityEnded } from '../models/agent-activity.model';

interface CapturedHubCallbacks {
  onWorkerActivity: ((activity: WorkerActivity) => void) | null;
  onAgentActivity?: ((activity: AgentActivity) => void) | null;
  onAgentActivityEnded?: ((ended: AgentActivityEnded) => void) | null;
  onReconnected: (() => void) | null;
}

function buildMockHub(captured: CapturedHubCallbacks): WorkerHub {
  return {
    on: <T>(method: string, cb: (payload: T) => void) => {
      if (method === 'WorkerActivity') {
        captured.onWorkerActivity = cb as unknown as (activity: WorkerActivity) => void;
      } else if (method === 'AgentActivity') {
        captured.onAgentActivity = cb as unknown as (activity: AgentActivity) => void;
      } else if (method === 'AgentActivityEnded') {
        captured.onAgentActivityEnded = cb as unknown as (ended: AgentActivityEnded) => void;
      }
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

function buildAgentActivity(overrides: Partial<AgentActivity> = {}): AgentActivity {
  return {
    workerRunId: 'run-1',
    issueId: 'issue-1',
    lastEventAt: '2026-01-01T00:00:00Z',
    tasks: [],
    currentTask: null,
    completedTaskCount: 0,
    subagents: [],
    activeSkill: null,
    inFlightTool: null,
    tokens: { inputTokens: 0, outputTokens: 0, cacheReadInputTokens: 0, cacheCreationInputTokens: 0 },
    ...overrides,
  };
}

describe('WorkerSignalRService', () => {
  describe('agent activity', () => {
    it('should return null for an issue with no agent activity', () => {
      // Arrange / Act
      const { svc } = setup();

      // Assert
      expect(svc.agentActivityForIssue('issue-1')).toBeNull();
    });

    it('should store the latest AgentActivity payload by issue id', () => {
      // Arrange
      const { svc, captured } = setup();
      const first = buildAgentActivity({ completedTaskCount: 1 });
      const second = buildAgentActivity({ completedTaskCount: 2 });

      // Act
      captured.onAgentActivity!(first);
      captured.onAgentActivity!(second);

      // Assert
      expect(svc.agentActivityForIssue('issue-1')).toEqual(second);
    });

    it('should remove the agent activity when AgentActivityEnded arrives for the same run', () => {
      // Arrange
      const { svc, captured } = setup();
      captured.onAgentActivity!(buildAgentActivity());

      // Act
      captured.onAgentActivityEnded!({ workerRunId: 'run-1', issueId: 'issue-1' });

      // Assert
      expect(svc.agentActivityForIssue('issue-1')).toBeNull();
    });

    it('should keep a newer run\'s activity when AgentActivityEnded arrives for an older run', () => {
      // Arrange
      const { svc, captured } = setup();
      const newer = buildAgentActivity({ workerRunId: 'run-2' });
      captured.onAgentActivity!(newer);

      // Act
      captured.onAgentActivityEnded!({ workerRunId: 'run-1', issueId: 'issue-1' });

      // Assert
      expect(svc.agentActivityForIssue('issue-1')).toEqual(newer);
    });
  });

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
  it('should return null from commitCountForIssue for an unknown issueId before any activity arrives', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert — null signals pre-handshake state (unknown), distinct from observed 0
    expect(svc.commitCountForIssue('unknown-issue')).toBeNull();
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

  // Finding 3: commitCountForIssue returns null for unknown issues (pre-handshake)
  it('should return null from commitCountForIssue for an unknown issueId (pre-handshake state)', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert — null distinguishes "not yet observed" from observed 0
    expect(svc.commitCountForIssue('unknown-issue')).toBeNull();
  });

  it('should return 0 from commitCountForIssue when WorkerActivity arrives with commitCount 0', () => {
    // Arrange
    const { svc, captured } = setup();
    const activity: WorkerActivity = {
      workerRunId: 'run-G',
      issueId: 'issue-G',
      lastActivityAt: '2026-06-01T19:00:00Z',
      commitCount: 0,
    };

    // Act
    captured.onWorkerActivity!(activity);

    // Assert — 0 is a real observed value, not null
    expect(svc.commitCountForIssue('issue-G')).toBe(0);
  });

  // Finding 2: commitCountForIssue and activityForIssue are reactive — reading via workerActivity signal
  it('should reflect new commitCount reactively after WorkerActivity arrives', () => {
    // Arrange
    const { svc, captured } = setup();
    const initialNull = svc.commitCountForIssue('issue-H');

    // Act
    captured.onWorkerActivity!({
      workerRunId: 'run-H',
      issueId: 'issue-H',
      lastActivityAt: '2026-06-01T20:00:00Z',
      commitCount: 5,
    });

    // Assert — value changes after push, no external tick needed
    expect(initialNull).toBeNull();
    expect(svc.commitCountForIssue('issue-H')).toBe(5);
  });

  it('should reflect new activityAt reactively after WorkerActivity arrives', () => {
    // Arrange
    const { svc, captured } = setup();
    const initialNull = svc.activityForIssue('issue-I');

    // Act
    captured.onWorkerActivity!({
      workerRunId: 'run-I',
      issueId: 'issue-I',
      lastActivityAt: '2026-06-01T21:00:00Z',
      commitCount: 2,
    });

    // Assert
    expect(initialNull).toBeNull();
    expect(svc.activityForIssue('issue-I')).toBe('2026-06-01T21:00:00Z');
  });
});
