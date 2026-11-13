import { TestBed } from '@angular/core/testing';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { HUB_TRANSPORT_FACTORY, HubConnections, STABLE_CONNECTION_MS } from './hub-connection';
import { FakeHubTransport, provideFakeHubs } from '../testing/fake-hub-transport';

function setup(hub = new FakeHubTransport()) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ providers: [provideFakeHubs({ issues: hub })] });
  return { connection: TestBed.inject(HubConnections).get('issues'), hub };
}

function accept(hub: FakeHubTransport): void {
  hub.startResult = () => Promise.resolve();
}

function failingStarts(hub: FakeHubTransport): void {
  hub.startResult = () => Promise.reject(new Error('refused'));
}

/** The delays, in ms, between the start attempts the connection makes over `totalMs`. */
async function attemptGaps(hub: FakeHubTransport, totalMs: number): Promise<number[]> {
  const times: number[] = [];
  const startResult = hub.startResult;
  hub.startResult = () => {
    times.push(Date.now());
    return startResult();
  };
  const begin = Date.now();
  await vi.advanceTimersByTimeAsync(totalMs);
  return times.map((time, i) => time - (i === 0 ? begin : times[i - 1]));
}

describe('HUB_TRANSPORT_FACTORY default', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('should build each hub at /hubs/{name} without SignalR automatic reconnect', () => {
    // Arrange
    const withUrl = vi.spyOn(HubConnectionBuilder.prototype, 'withUrl');
    const withAutomaticReconnect = vi.spyOn(HubConnectionBuilder.prototype, 'withAutomaticReconnect');
    // The test environment has no base URL to resolve the hub path against, so stop short of building.
    vi.spyOn(HubConnectionBuilder.prototype, 'build').mockReturnValue({} as HubConnection);
    TestBed.configureTestingModule({});
    const factory = TestBed.inject(HUB_TRANSPORT_FACTORY);

    // Act
    factory('issues');
    factory('system');
    factory('workers');

    // Assert
    expect(withUrl.mock.calls.map((call) => call[0])).toEqual(['/hubs/issues', '/hubs/system', '/hubs/workers']);
    expect(withAutomaticReconnect).not.toHaveBeenCalled();
  });
});

describe('HubConnections', () => {
  it('should hand out one shared connection per hub', () => {
    // Arrange
    TestBed.resetTestingModule();
    const created: string[] = [];
    TestBed.configureTestingModule({
      providers: [{
        provide: HUB_TRANSPORT_FACTORY,
        useValue: (hub: string) => {
          created.push(hub);
          return new FakeHubTransport();
        },
      }],
    });
    const connections = TestBed.inject(HubConnections);

    // Act
    const first = connections.get('system');
    const second = connections.get('system');
    const workers = connections.get('workers');

    // Assert
    expect(second).toBe(first);
    expect(workers).not.toBe(first);
    expect(created).toEqual(['system', 'workers']);
  });

  it('should stop retrying once the injector is destroyed', async () => {
    // Arrange
    vi.useFakeTimers();
    const { connection, hub } = setup();
    await connection.start();
    failingStarts(hub);
    hub.close();
    TestBed.resetTestingModule();
    const startsBefore = hub.startCount;

    // Act
    await vi.advanceTimersByTimeAsync(120_000);

    // Assert
    expect(hub.startCount).toBe(startsBefore);
    vi.useRealTimers();
  });
});

describe('SharedHubConnection', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('should start disconnected and not start the transport by itself', () => {
    // Arrange / Act
    const { connection, hub } = setup();

    // Assert
    expect(connection.status()).toBe('disconnected');
    expect(hub.startCount).toBe(0);
  });

  it('should report connected once start resolves', async () => {
    // Arrange
    const { connection } = setup();

    // Act
    await connection.start();

    // Assert
    expect(connection.status()).toBe('connected');
    expect(connection.nextAttemptAt()).toBeNull();
  });

  it('should start the transport once however often start is called', async () => {
    // Arrange
    const { connection, hub } = setup();

    // Act
    await Promise.all([connection.start(), connection.start()]);

    // Assert
    expect(hub.startCount).toBe(1);
  });

  it('should report attempting while a start is in flight', async () => {
    // Arrange
    const hub = new FakeHubTransport();
    let resolveStart!: () => void;
    hub.startResult = () => new Promise<void>((resolve) => { resolveStart = resolve; });
    const { connection } = setup(hub);

    // Act
    const started = connection.start();
    const during = connection.attempting();
    resolveStart();
    await started;

    // Assert
    expect(during).toBe(true);
    expect(connection.attempting()).toBe(false);
  });

  it('should reject a failed first start, report reconnecting and retry after 1 s', async () => {
    // Arrange
    vi.useFakeTimers();
    const { connection, hub } = setup();
    const failure = new Error('refused');
    hub.startResult = () => Promise.reject(failure);

    // Act
    const result = connection.start();

    // Assert
    await expect(result).rejects.toBe(failure);
    expect(connection.status()).toBe('reconnecting');
    expect(connection.nextAttemptAt()).toBe(Date.now() + 1_000);
    await vi.advanceTimersByTimeAsync(999);
    expect(hub.startCount).toBe(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(hub.startCount).toBe(2);
  });

  it('should keep retrying a failed first start on the backoff schedule', async () => {
    // Arrange
    vi.useFakeTimers();
    vi.spyOn(Math, 'random').mockReturnValue(0.5);
    const { connection, hub } = setup();
    failingStarts(hub);
    await connection.start().catch(() => undefined);

    // Act
    const gaps = await attemptGaps(hub, 1_000 + 2_000 + 5_000 + 10_000 + 30_000 + 30_000);

    // Assert
    expect(gaps).toEqual([1_000, 2_000, 5_000, 10_000, 30_000, 30_000]);
  });

  it('should retry a dropped connection at once, then on the backoff schedule, with no limit', async () => {
    // Arrange
    vi.useFakeTimers();
    vi.spyOn(Math, 'random').mockReturnValue(0.5);
    const { connection, hub } = setup();
    await connection.start();
    failingStarts(hub);

    // Act
    hub.close();
    const gaps = await attemptGaps(hub, 18_000 + 30_000 * 20);

    // Assert
    expect(connection.status()).toBe('reconnecting');
    expect(gaps.slice(0, 6)).toEqual([0, 1_000, 2_000, 5_000, 10_000, 30_000]);
    expect(gaps.length).toBe(25);
    expect(gaps.slice(5).every((gap) => gap === 30_000)).toBe(true);
  });

  it('should keep backing off when a connection made on a retry drops at once', async () => {
    // Arrange
    vi.useFakeTimers();
    vi.spyOn(Math, 'random').mockReturnValue(0.5);
    const { connection, hub } = setup();
    await connection.start();
    let connects = 0;
    hub.startResult = () => {
      connects++;
      // Every attempt connects, and the server closes the connection straight away.
      setTimeout(() => hub.close(), 0);
      return Promise.resolve();
    };

    // Act
    hub.close();
    const times: number[] = [];
    const begin = Date.now();
    const connect = hub.startResult;
    hub.startResult = () => {
      times.push(Date.now() - begin);
      return connect();
    };
    await vi.advanceTimersByTimeAsync(9_000);

    // Assert: at once, then 1, 2 and 5 s apart, not a 0 ms loop.
    // Each close lands a timer tick (1 ms) after its connect.
    expect(times.map((t) => Math.round(t / 1_000) * 1_000)).toEqual([0, 1_000, 3_000, 8_000]);
    expect(connects).toBe(4);
  });

  it('should retry at once again after a connection made on a retry stayed up', async () => {
    // Arrange
    vi.useFakeTimers();
    const { connection, hub } = setup();
    await connection.start();
    failingStarts(hub);
    hub.close();
    await vi.advanceTimersByTimeAsync(0);
    accept(hub);
    await vi.advanceTimersByTimeAsync(1_000);
    await vi.advanceTimersByTimeAsync(STABLE_CONNECTION_MS);
    const before = hub.startCount;

    // Act
    hub.close();
    await vi.advanceTimersByTimeAsync(0);

    // Assert
    expect(hub.startCount).toBe(before + 1);
  });

  it('should jitter the steady 30 s retry by up to 20 %', async () => {
    // Arrange
    vi.useFakeTimers();
    vi.spyOn(Math, 'random').mockReturnValue(0);
    const { connection, hub } = setup();
    await connection.start();
    failingStarts(hub);
    hub.close();

    // Act
    const gaps = await attemptGaps(hub, 18_000 + 24_000);

    // Assert
    expect(gaps[5]).toBe(24_000);
  });

  it('should report when the next attempt is due, and nothing while attempting', async () => {
    // Arrange
    vi.useFakeTimers();
    const { connection, hub } = setup();
    await connection.start();
    let rejectStart!: (error: Error) => void;
    hub.startResult = () => new Promise<void>((_, reject) => { rejectStart = reject; });
    hub.close();

    // Act
    await vi.advanceTimersByTimeAsync(0);
    const during = connection.nextAttemptAt();
    rejectStart(new Error('refused'));
    await vi.advanceTimersByTimeAsync(0);

    // Assert
    expect(during).toBeNull();
    expect(connection.nextAttemptAt()).toBe(Date.now() + 1_000);
  });

  it('should cancel the wait and attempt at once on tryNow', async () => {
    // Arrange
    vi.useFakeTimers();
    const { connection, hub } = setup();
    failingStarts(hub);
    await connection.start().catch(() => undefined);
    await vi.advanceTimersByTimeAsync(1_000 + 2_000 + 5_000);
    const startsBefore = hub.startCount;
    hub.startResult = () => Promise.resolve();

    // Act
    connection.tryNow();
    await vi.advanceTimersByTimeAsync(0);

    // Assert
    expect(hub.startCount).toBe(startsBefore + 1);
    expect(connection.status()).toBe('connected');
    await vi.advanceTimersByTimeAsync(60_000);
    expect(hub.startCount).toBe(startsBefore + 1);
  });

  it('should ignore tryNow while an attempt is in flight', async () => {
    // Arrange
    vi.useFakeTimers();
    const hub = new FakeHubTransport();
    hub.startResult = () => new Promise<void>(() => undefined);
    const { connection } = setup(hub);
    void connection.start();

    // Act
    connection.tryNow();

    // Assert
    expect(hub.startCount).toBe(1);
  });

  it('should ignore tryNow while connected or before start', async () => {
    // Arrange
    const { connection, hub } = setup();

    // Act
    connection.tryNow();
    await connection.start();
    connection.tryNow();

    // Assert
    expect(hub.startCount).toBe(1);
  });

  it('should fire every connected callback after a reconnect, once the status is connected', async () => {
    // Arrange
    vi.useFakeTimers();
    const { connection, hub } = setup();
    await connection.start();
    const seen: string[] = [];
    connection.onConnected(() => seen.push(`first:${connection.status()}`));
    connection.onConnected(() => seen.push(`second:${connection.status()}`));

    // Act
    hub.close();
    await vi.advanceTimersByTimeAsync(0);

    // Assert
    expect(seen).toEqual(['first:connected', 'second:connected']);
  });

  it('should fire the connected event when a first start succeeds only on a retry', async () => {
    // Arrange
    vi.useFakeTimers();
    const { connection, hub } = setup();
    failingStarts(hub);
    let fired = 0;
    connection.onConnected(() => fired++);
    await connection.start().catch(() => undefined);
    hub.startResult = () => Promise.resolve();

    // Act
    await vi.advanceTimersByTimeAsync(1_000);

    // Assert
    expect(fired).toBe(1);
  });

  it('should not fire the connected event when the first start succeeds at once', async () => {
    // Arrange
    const { connection } = setup();
    let fired = 0;
    connection.onConnected(() => fired++);

    // Act
    await connection.start();

    // Assert
    expect(fired).toBe(0);
  });

  it('should deliver messages to handlers registered before start resolves', async () => {
    // Arrange
    const hub = new FakeHubTransport();
    let resolveStart!: () => void;
    hub.startResult = () => new Promise<void>((resolve) => { resolveStart = resolve; });
    const { connection } = setup(hub);
    const received: number[] = [];
    connection.on<number>('Tick', (n) => received.push(n));
    const started = connection.start();

    // Act
    hub.emit('Tick', 1);
    resolveStart();
    await started;
    hub.emit('Tick', 2);

    // Assert
    expect(received).toEqual([1, 2]);
  });

  it('should keep delivering messages after a reconnect', async () => {
    // Arrange
    vi.useFakeTimers();
    const { connection, hub } = setup();
    const received: number[] = [];
    connection.on<number>('Tick', (n) => received.push(n));
    await connection.start();
    hub.close();
    await vi.advanceTimersByTimeAsync(0);

    // Act
    hub.emit('Tick', 7);

    // Assert
    expect(connection.status()).toBe('connected');
    expect(received).toEqual([7]);
  });

  it('should stop delivering to a handler removed with off', () => {
    // Arrange
    const { connection, hub } = setup();
    const received: number[] = [];
    const handler = (n: number) => received.push(n);
    connection.on<number>('Tick', handler);

    // Act
    connection.off<number>('Tick', handler);
    hub.emit('Tick', 1);

    // Assert
    expect(received).toEqual([]);
  });

  it('should pass a stream request and its arguments through to the transport', () => {
    // Arrange
    const { connection, hub } = setup();
    const lines: string[] = [];

    // Act
    const subscription = connection.stream<string>('StreamLog', 'run-1').subscribe({
      next: (line) => lines.push(line),
      error: () => {},
      complete: () => {},
    });
    hub.streams[0].subscriber!.next('hello');
    subscription.dispose();

    // Assert
    expect(hub.streams[0].methodName).toBe('StreamLog');
    expect(hub.streams[0].args).toEqual(['run-1']);
    expect(lines).toEqual(['hello']);
    expect(hub.streams[0].disposed).toBe(true);
  });

  it('should pass an invocation through to the transport and return its result', async () => {
    // Arrange
    const { connection, hub } = setup();
    hub.invokeResult = () => Promise.resolve(42);

    // Act
    const result = await connection.invoke<number>('GetAnswer', 'x');

    // Assert
    expect(result).toBe(42);
    expect(hub.invocations).toEqual([{ methodName: 'GetAnswer', args: ['x'] }]);
  });
});
