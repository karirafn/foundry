import { TestBed } from '@angular/core/testing';
import { LoadOutcome } from '../models/load-outcome';
import { FakeHubTransport, provideFakeHubs } from '../testing/fake-hub-transport';
import { ConnectionGateService, GATE_GRACE_MS, MIN_RELOADING_MS } from './connection-gate.service';
import { HubConnections, HubName } from './hub-connection';

type Hubs = Record<HubName, FakeHubTransport>;

function setup(beforeStart: (hubs: Hubs) => void = () => undefined) {
  vi.useFakeTimers();
  vi.spyOn(Math, 'random').mockReturnValue(0.5);
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
  const hubs: Hubs = {
    issues: new FakeHubTransport(),
    system: new FakeHubTransport(),
    workers: new FakeHubTransport(),
  };
  hubs.workers.invokeResult = () => Promise.resolve({ workerActivities: [], agentActivities: [] });
  beforeStart(hubs);
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ providers: [provideFakeHubs(hubs)] });
  const gate = TestBed.inject(ConnectionGateService);
  return { gate, hubs };
}

/** Lets timers and promise callbacks run for `ms`, then flushes effects. */
async function elapse(ms = 0): Promise<void> {
  await vi.advanceTimersByTimeAsync(ms);
  TestBed.tick();
  await vi.advanceTimersByTimeAsync(0);
  TestBed.tick();
}

function refuse(hub: FakeHubTransport): void {
  hub.startResult = () => Promise.reject(new Error('refused'));
}

function accept(hub: FakeHubTransport): void {
  hub.startResult = () => Promise.resolve();
}

/** Drops `hub` with every retry refused, and waits out the grace period so the gate is up. */
async function raiseGate(hub: FakeHubTransport): Promise<void> {
  refuse(hub);
  hub.close();
  await elapse(GATE_GRACE_MS);
}

describe('ConnectionGateService', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('should be in grace until the hubs first connect, then live', async () => {
    // Arrange
    const { gate } = setup();
    TestBed.tick();
    const before = gate.phase();

    // Act
    await elapse();

    // Assert
    expect(before).toBe('grace');
    expect(gate.phase()).toBe('live');
    expect(gate.up()).toBe(false);
  });

  it('should rise when a hub never connects at app start', async () => {
    // Arrange
    const { gate } = setup((hubs) => refuse(hubs.system));

    // Act
    await elapse(GATE_GRACE_MS);

    // Assert
    expect(gate.phase()).toBe('reconnecting');
    expect(gate.up()).toBe(true);
  });

  it('should leave the page as it is for the grace period after a drop', async () => {
    // Arrange
    const { gate, hubs } = setup();
    await elapse();
    refuse(hubs.issues);

    // Act
    hubs.issues.close();
    await elapse(GATE_GRACE_MS - 1);

    // Assert
    expect(gate.phase()).toBe('grace');
    expect(gate.up()).toBe(false);
  });

  it('should rise once a hub has been down for the grace period', async () => {
    // Arrange
    const { gate, hubs } = setup();
    await elapse();

    // Act
    await raiseGate(hubs.workers);

    // Assert
    expect(gate.phase()).toBe('reconnecting');
    expect(gate.up()).toBe(true);
  });

  it('should never rise for a hub that recovers within the grace period', async () => {
    // Arrange
    const { gate, hubs } = setup();
    await elapse();
    refuse(hubs.issues);
    hubs.issues.close();
    await elapse(1_000);

    // Act
    accept(hubs.issues);
    await elapse(1_000);
    await elapse(GATE_GRACE_MS);

    // Assert
    expect(gate.phase()).toBe('live');
  });

  it('should time the grace period per hub', async () => {
    // Arrange
    const { gate, hubs } = setup();
    await elapse();
    refuse(hubs.issues);
    hubs.issues.close();
    await elapse(1_500);
    refuse(hubs.system);
    hubs.system.close();
    accept(hubs.issues);
    TestBed.inject(HubConnections).get('issues').tryNow();
    await elapse(500);

    // Act
    await elapse(1_000);
    const atIssuesDeadline = gate.phase();
    await elapse(1_000);

    // Assert
    expect(atIssuesDeadline).toBe('grace');
    expect(gate.phase()).toBe('reconnecting');
  });

  it('should report trying while a down hub has an attempt in flight', async () => {
    // Arrange
    const { gate, hubs } = setup();
    await elapse();
    await raiseGate(hubs.issues);
    hubs.issues.startResult = () => new Promise<void>(() => undefined);

    // Act
    gate.tryNow();
    TestBed.tick();

    // Assert
    expect(gate.phase()).toBe('trying');
    expect(gate.nextAttemptAt()).toBeNull();
  });

  it('should report the earliest attempt due among the down hubs', async () => {
    // Arrange
    const { gate, hubs } = setup();
    await elapse();

    // Act
    await raiseGate(hubs.issues);

    // Assert: attempts at 0 s, 1 s fail; the third is due 2 s after the second.
    expect(gate.nextAttemptAt()).toBe(Date.now() + 1_000);
  });

  it('should attempt every down hub at once on tryNow', async () => {
    // Arrange
    const { gate, hubs } = setup();
    await elapse();
    refuse(hubs.system);
    hubs.system.close();
    await raiseGate(hubs.workers);
    const before = [hubs.system.startCount, hubs.workers.startCount];

    // Act
    gate.tryNow();

    // Assert
    expect([hubs.system.startCount, hubs.workers.startCount]).toEqual([
      before[0] + 1,
      before[1] + 1,
    ]);
  });

  it('should stay up while any hub is still down, lighting the connected ones', async () => {
    // Arrange
    const { gate, hubs } = setup();
    await elapse();
    refuse(hubs.system);
    hubs.system.close();
    await raiseGate(hubs.workers);

    // Act
    accept(hubs.system);
    gate.tryNow();
    await elapse();

    // Assert
    expect(gate.up()).toBe(true);
    expect(gate.connectedHubs()).toEqual({ issues: true, system: true, workers: false });
  });

  it('should go live at once when every hub is back and no page is gated', async () => {
    // Arrange
    const { gate, hubs } = setup();
    await elapse();
    await raiseGate(hubs.issues);

    // Act
    accept(hubs.issues);
    gate.tryNow();
    await elapse();

    // Assert
    expect(gate.phase()).toBe('live');
  });

  it('should reload the gated page once every hub is back, for at least the minimum time', async () => {
    // Arrange
    const { gate, hubs } = setup();
    await elapse();
    const reload = vi.fn(() => Promise.resolve<LoadOutcome>('loaded'));
    gate.register(reload);
    await raiseGate(hubs.issues);

    // Act
    accept(hubs.issues);
    gate.tryNow();
    await elapse();
    const reloading = gate.phase();
    await elapse(MIN_RELOADING_MS - 1);
    const beforeMinimum = gate.phase();
    await elapse(1);

    // Assert
    expect(reload).toHaveBeenCalledTimes(1);
    expect(reloading).toBe('reloading');
    expect(beforeMinimum).toBe('reloading');
    expect(gate.phase()).toBe('live');
  });

  it('should hold reloading until a slow reload completes', async () => {
    // Arrange
    const { gate, hubs } = setup();
    await elapse();
    let finish!: (outcome: LoadOutcome) => void;
    gate.register(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    await raiseGate(hubs.issues);
    accept(hubs.issues);
    gate.tryNow();
    await elapse(5_000);
    const waiting = gate.phase();

    // Act
    finish('loaded');
    await elapse();

    // Assert
    expect(waiting).toBe('reloading');
    expect(gate.phase()).toBe('live');
  });

  it('should retry a failed reload on the backoff, and at once on tryNow', async () => {
    // Arrange
    const { gate, hubs } = setup();
    await elapse();
    const outcomes: LoadOutcome[] = ['failed', 'failed', 'loaded'];
    const reload = vi.fn(() => Promise.resolve(outcomes.shift()!));
    gate.register(reload);
    await raiseGate(hubs.issues);
    accept(hubs.issues);
    gate.tryNow();
    await elapse(MIN_RELOADING_MS);
    const failed = gate.phase();
    const retryDelay = gate.nextAttemptAt()! - Date.now();
    await elapse(1_000);
    const retried = reload.mock.calls.length;
    await elapse(MIN_RELOADING_MS);

    // Act
    gate.tryNow();
    await elapse(MIN_RELOADING_MS);

    // Assert
    expect(failed).toBe('reload-failed');
    expect(retryDelay).toBe(1_000);
    expect(retried).toBe(2);
    expect(reload).toHaveBeenCalledTimes(3);
    expect(gate.phase()).toBe('live');
  });

  it('should return to reconnecting when a hub drops while reloading', async () => {
    // Arrange
    const { gate, hubs } = setup();
    await elapse();
    gate.register(() => new Promise<LoadOutcome>(() => undefined));
    await raiseGate(hubs.issues);
    accept(hubs.issues);
    gate.tryNow();
    await elapse();

    // Act
    refuse(hubs.workers);
    hubs.workers.close();
    await elapse();

    // Assert
    expect(gate.phase()).toBe('reconnecting');
  });

  it('should go live when the gated page leaves while reloading', async () => {
    // Arrange
    const { gate, hubs } = setup();
    await elapse();
    const unregister = gate.register(() => new Promise<LoadOutcome>(() => undefined));
    await raiseGate(hubs.issues);
    accept(hubs.issues);
    gate.tryNow();
    await elapse();

    // Act
    unregister();

    // Assert
    expect(gate.phase()).toBe('live');
  });
});
