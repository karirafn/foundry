import { TestBed } from '@angular/core/testing';
import { firstValueFrom, take, toArray } from 'rxjs';
import { CurrentWorkerActivity, RECENT_TOOL_LIMIT, WorkerSignalRService } from './worker-signalr.service';
import { FakeHubTransport, provideFakeHubs } from '../testing/fake-hub-transport';
import { WorkerActivity } from '../models/worker-activity.model';
import { AgentActivity, AgentActivityEnded } from '../models/agent-activity.model';

const NO_ACTIVITY: CurrentWorkerActivity = { workerActivities: [], agentActivities: [] };

function setup(current: CurrentWorkerActivity = NO_ACTIVITY) {
  TestBed.resetTestingModule();

  const hub = new FakeHubTransport();
  hub.invokeResult = () => Promise.resolve(current);

  TestBed.configureTestingModule({
    providers: [
      WorkerSignalRService,
      provideFakeHubs({ workers: hub }),
    ],
  });

  return {
    svc: TestBed.inject(WorkerSignalRService),
    hub,
  };
}

function buildAgentActivity(overrides: Partial<AgentActivity> = {}): AgentActivity {
  return {
    workerRunId: 'run-1',
    issueId: 'issue-1',
    role: 'implement',
    lastEventAt: '2026-01-01T00:00:00Z',
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

describe('WorkerSignalRService', () => {
  describe('floor support', () => {
    afterEach(() => {
      vi.useRealTimers();
    });

    it('should list every agent activity and count active agents', () => {
      // Arrange
      const { svc, hub } = setup();

      // Act
      hub.emit('AgentActivity', buildAgentActivity({ workerRunId: 'run-1', issueId: 'issue-1' }));
      hub.emit('AgentActivity', buildAgentActivity({ workerRunId: 'run-2', issueId: 'issue-2' }));

      // Assert
      expect(svc.activeAgentCount()).toBe(2);
      expect(svc.agentActivities().map((a) => a.workerRunId)).toEqual(['run-1', 'run-2']);
    });

    it('should decrement the active agent count when a run ends', () => {
      // Arrange
      const { svc, hub } = setup();
      hub.emit('AgentActivity', buildAgentActivity({ workerRunId: 'run-1', issueId: 'issue-1' }));

      // Act
      hub.emit('AgentActivityEnded', { workerRunId: 'run-1', issueId: 'issue-1' });

      // Assert
      expect(svc.activeAgentCount()).toBe(0);
    });

    it('should accumulate distinct in-flight tools into the recent tool ticker', () => {
      // Arrange
      const { svc, hub } = setup();
      const bash = { name: 'Bash', summary: 'npm test', startedAt: '2026-01-01T00:00:01Z' };
      const read = { name: 'Read', summary: 'src/app.ts', startedAt: '2026-01-01T00:00:05Z' };

      // Act
      hub.emit('AgentActivity', buildAgentActivity({ inFlightTool: bash }));
      hub.emit('AgentActivity', buildAgentActivity({ inFlightTool: bash }));
      hub.emit('AgentActivity', buildAgentActivity({ inFlightTool: null }));
      hub.emit('AgentActivity', buildAgentActivity({ inFlightTool: read }));

      // Assert
      expect(svc.recentToolsForRun('run-1')).toEqual([bash, read]);
    });

    it('should keep only the most recent tools', () => {
      // Arrange
      const { svc, hub } = setup();

      // Act
      for (let i = 0; i < RECENT_TOOL_LIMIT + 2; i++) {
        hub.emit('AgentActivity', buildAgentActivity({
          inFlightTool: { name: 'Bash', summary: `step ${i}`, startedAt: null },
        }));
      }

      // Assert
      const tools = svc.recentToolsForRun('run-1');
      expect(tools.length).toBe(RECENT_TOOL_LIMIT);
      expect(tools[tools.length - 1].summary).toBe(`step ${RECENT_TOOL_LIMIT + 1}`);
    });

    it('should forget the tool ticker when the run ends', () => {
      // Arrange
      const { svc, hub } = setup();
      hub.emit('AgentActivity', buildAgentActivity({ inFlightTool: { name: 'Bash', summary: null, startedAt: null } }));

      // Act
      hub.emit('AgentActivityEnded', { workerRunId: 'run-1', issueId: 'issue-1' });

      // Assert
      expect(svc.recentToolsForRun('run-1')).toEqual([]);
    });

    it('should report connected after the hub starts, and reconnecting after a drop', async () => {
      // Arrange
      vi.useFakeTimers();
      const { svc, hub } = setup();
      hub.startResult = () => new Promise<void>(() => undefined);
      await vi.advanceTimersByTimeAsync(0);
      const afterStart = svc.connectionStatus();

      // Act
      hub.close();

      // Assert
      expect(afterStart).toBe('connected');
      expect(svc.connectionStatus()).toBe('reconnecting');
    });

    it('should load the current activity once connected', async () => {
      // Arrange / Act
      const { svc, hub } = setup({
        workerActivities: [{ workerRunId: 'run-1', issueId: 'issue-1', lastActivityAt: '2026-06-01T12:00:00Z' }],
        agentActivities: [buildAgentActivity({ workerRunId: 'run-1', issueId: 'issue-1' })],
      });
      await vi.waitFor(() => expect(svc.activeAgentCount()).toBe(1));

      // Assert
      expect(hub.invocations.map((i) => i.methodName)).toEqual(['GetCurrentActivity']);
      expect(svc.activityForIssue('issue-1')).toBe('2026-06-01T12:00:00Z');
    });

    it('should drop runs that ended while disconnected when it reconnects', async () => {
      // Arrange
      vi.useFakeTimers();
      const { svc, hub } = setup();
      await vi.advanceTimersByTimeAsync(0);
      hub.emit('AgentActivity', buildAgentActivity({ workerRunId: 'run-1', issueId: 'issue-1', inFlightTool: { name: 'Bash', summary: null, startedAt: null } }));
      hub.emit('AgentActivity', buildAgentActivity({ workerRunId: 'run-2', issueId: 'issue-2', inFlightTool: { name: 'Bash', summary: null, startedAt: null } }));
      // run-2 ended while disconnected, so the hub reports only run-1.
      hub.invokeResult = () => Promise.resolve({
        workerActivities: [],
        agentActivities: [buildAgentActivity({ workerRunId: 'run-1', issueId: 'issue-1' })],
      });

      // Act
      hub.close();
      await vi.advanceTimersByTimeAsync(0);

      // Assert
      expect(svc.agentActivities().map((a) => a.workerRunId)).toEqual(['run-1']);
      expect(svc.recentToolsForRun('run-2')).toEqual([]);
    });

    it('should keep a message that arrives during the load when it is newer than the snapshot', async () => {
      // Arrange
      const { svc, hub } = setup();
      let resolveLoad!: (current: CurrentWorkerActivity) => void;
      hub.invokeResult = () => new Promise((resolve) => { resolveLoad = resolve as (c: CurrentWorkerActivity) => void; });
      const load = svc.loadActivity();
      const newer = buildAgentActivity({ issueId: 'issue-1', lastEventAt: '2026-01-01T00:05:00Z', completedTaskCount: 3 });

      // Act
      hub.emit('AgentActivity', newer);
      resolveLoad({ workerActivities: [], agentActivities: [buildAgentActivity({ issueId: 'issue-1', lastEventAt: '2026-01-01T00:01:00Z' })] });
      await load;

      // Assert
      expect(svc.agentActivityForIssue('issue-1')).toEqual(newer);
    });

    it('should drop a run whose end arrives during the load', async () => {
      // Arrange
      const { svc, hub } = setup();
      let resolveLoad!: (current: CurrentWorkerActivity) => void;
      hub.invokeResult = () => new Promise((resolve) => { resolveLoad = resolve as (c: CurrentWorkerActivity) => void; });
      const load = svc.loadActivity();

      // Act
      hub.emit('AgentActivityEnded', { workerRunId: 'run-1', issueId: 'issue-1' });
      resolveLoad({ workerActivities: [], agentActivities: [buildAgentActivity()] });
      await load;

      // Assert
      expect(svc.activeAgentCount()).toBe(0);
    });

    it('should share a load already in flight and report its outcome', async () => {
      // Arrange
      const { svc, hub } = setup();
      hub.invokeResult = () => Promise.resolve(NO_ACTIVITY);

      // Act
      const first = svc.loadActivity();
      const second = svc.loadActivity();

      // Assert
      expect(second).toBe(first);
      expect(await first).toBe('loaded');
      expect(hub.invocations.filter((i) => i.methodName === 'GetCurrentActivity').length).toBe(1);
    });

    it('should report a failed load and keep what it holds', async () => {
      // Arrange
      const { svc, hub } = setup();
      vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      hub.emit('AgentActivity', buildAgentActivity());
      hub.invokeResult = () => Promise.reject(new Error('down'));

      // Act
      const outcome = await svc.loadActivity();

      // Assert
      expect(outcome).toBe('failed');
      expect(svc.activeAgentCount()).toBe(1);
    });
  });

  describe('agent activity', () => {
    it('should return null for an issue with no agent activity', () => {
      // Arrange / Act
      const { svc } = setup();

      // Assert
      expect(svc.agentActivityForIssue('issue-1')).toBeNull();
    });

    it('should store the latest AgentActivity payload by issue id', () => {
      // Arrange
      const { svc, hub } = setup();
      const first = buildAgentActivity({ completedTaskCount: 1 });
      const second = buildAgentActivity({ completedTaskCount: 2 });

      // Act
      hub.emit('AgentActivity', first);
      hub.emit('AgentActivity', second);

      // Assert
      expect(svc.agentActivityForIssue('issue-1')).toEqual(second);
    });

    it('should remove the agent activity when AgentActivityEnded arrives for the same run', () => {
      // Arrange
      const { svc, hub } = setup();
      hub.emit('AgentActivity', buildAgentActivity());

      // Act
      hub.emit('AgentActivityEnded', { workerRunId: 'run-1', issueId: 'issue-1' });

      // Assert
      expect(svc.agentActivityForIssue('issue-1')).toBeNull();
    });

    it('should keep a newer run\'s activity when AgentActivityEnded arrives for an older run', () => {
      // Arrange
      const { svc, hub } = setup();
      const newer = buildAgentActivity({ workerRunId: 'run-2' });
      hub.emit('AgentActivity', newer);

      // Act
      hub.emit('AgentActivityEnded', { workerRunId: 'run-1', issueId: 'issue-1' });

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
    const { svc, hub } = setup();
    const activity: WorkerActivity = {
      workerRunId: 'run-1',
      issueId: 'issue-1',
      lastActivityAt: '2026-01-01T00:00:00Z',
    };

    // Act
    hub.emit('WorkerActivity', activity);

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
    const { svc, hub } = setup();
    const first: WorkerActivity = { workerRunId: 'run-1', issueId: 'issue-1', lastActivityAt: '2026-01-01T00:00:00Z' };
    const second: WorkerActivity = { workerRunId: 'run-1', issueId: 'issue-1', lastActivityAt: '2026-01-01T00:01:00Z' };
    hub.emit('WorkerActivity', first);

    // Act
    hub.emit('WorkerActivity', second);

    // Assert
    expect(svc.workerActivity()).toEqual(second);
  });

  // Cycle 5: reconnected emits when hub reconnects
  it('should emit on reconnected when hub reconnects', async () => {
    // Arrange
    vi.useFakeTimers();
    const { svc, hub } = setup();
    await vi.advanceTimersByTimeAsync(0);
    let count = 0;
    svc.reconnected.subscribe(() => count++);
    hub.close();

    // Act
    await vi.advanceTimersByTimeAsync(0);

    // Assert
    expect(count).toBe(1);
    vi.useRealTimers();
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
    const { svc, hub } = setup();
    const activity: WorkerActivity = {
      workerRunId: 'run-A',
      issueId: 'issue-A',
      lastActivityAt: '2026-06-01T12:00:00Z',
    };

    // Act
    hub.emit('WorkerActivity', activity);

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
    const { svc, hub } = setup();
    const activity: WorkerActivity = {
      workerRunId: 'run-B',
      issueId: 'issue-B',
      lastActivityAt: '2026-06-01T13:00:00Z',
    };

    // Act
    hub.emit('WorkerActivity', activity);

    // Assert
    expect(svc.activityForIssue('issue-B')).toBe('2026-06-01T13:00:00Z');
    // The other issue is still null
    expect(svc.activityForIssue('issue-A')).toBeNull();
  });

  it('should independently track activity for two different issues', () => {
    // Arrange
    const { svc, hub } = setup();
    const activityA: WorkerActivity = {
      workerRunId: 'run-A',
      issueId: 'issue-A',
      lastActivityAt: '2026-06-01T12:00:00Z',
    };
    const activityB: WorkerActivity = {
      workerRunId: 'run-B',
      issueId: 'issue-B',
      lastActivityAt: '2026-06-01T14:00:00Z',
    };

    // Act
    hub.emit('WorkerActivity', activityA);
    hub.emit('WorkerActivity', activityB);

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

  it('should reflect new activityAt reactively after WorkerActivity arrives', () => {
    // Arrange
    const { svc, hub } = setup();
    const initialNull = svc.activityForIssue('issue-I');

    // Act
    hub.emit('WorkerActivity', {
      workerRunId: 'run-I',
      issueId: 'issue-I',
      lastActivityAt: '2026-06-01T21:00:00Z',
    });

    // Assert
    expect(initialNull).toBeNull();
    expect(svc.activityForIssue('issue-I')).toBe('2026-06-01T21:00:00Z');
  });

  it('should stream a run log through the workers hub and dispose it on unsubscribe', () => {
    // Arrange
    const { svc, hub } = setup();
    const lines: string[] = [];

    // Act
    const subscription = svc.streamLog('run-L').subscribe((line) => lines.push(line));
    const stream = hub.streams[0];
    stream.subscriber!.next('first line');
    subscription.unsubscribe();

    // Assert
    expect(stream.methodName).toBe('StreamLog');
    expect(stream.args).toEqual(['run-L']);
    expect(lines).toEqual(['first line']);
    expect(stream.disposed).toBe(true);
  });
});
