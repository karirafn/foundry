import { TestBed } from '@angular/core/testing';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { HUB_TRANSPORT_FACTORY, HubConnections } from './hub-connection';
import { FakeHubTransport, provideFakeHubs } from '../testing/fake-hub-transport';

function setup(hub = new FakeHubTransport()) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ providers: [provideFakeHubs({ issues: hub })] });
  return { connection: TestBed.inject(HubConnections).get('issues'), hub };
}

describe('HUB_TRANSPORT_FACTORY default', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('should build each hub at /hubs/{name} with the default automatic reconnect policy', () => {
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
    expect(withAutomaticReconnect).toHaveBeenCalledTimes(3);
    for (const call of withAutomaticReconnect.mock.calls) {
      expect(call).toEqual([]);
    }
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
});

describe('SharedHubConnection', () => {
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
  });

  it('should reject and stay disconnected when the first start fails, without retrying', async () => {
    // Arrange
    const { connection, hub } = setup();
    const failure = new Error('refused');
    hub.startResult = () => Promise.reject(failure);

    // Act
    const result = connection.start();

    // Assert
    await expect(result).rejects.toBe(failure);
    expect(connection.status()).toBe('disconnected');
    expect(hub.startCount).toBe(1);
  });

  it('should track reconnecting, reconnected and close', async () => {
    // Arrange
    const { connection, hub } = setup();
    await connection.start();
    const seen: string[] = [];

    // Act
    hub.reconnecting();
    seen.push(connection.status());
    hub.reconnected();
    seen.push(connection.status());
    hub.close();
    seen.push(connection.status());

    // Assert
    expect(seen).toEqual(['reconnecting', 'connected', 'disconnected']);
  });

  it('should fire every onReconnected callback after the status is connected again', async () => {
    // Arrange
    const { connection, hub } = setup();
    await connection.start();
    const seen: string[] = [];
    connection.onReconnected(() => seen.push(`first:${connection.status()}`));
    connection.onReconnected(() => seen.push(`second:${connection.status()}`));
    hub.reconnecting();

    // Act
    hub.reconnected();

    // Assert
    expect(seen).toEqual(['first:connected', 'second:connected']);
  });

  it('should fire onReconnecting callbacks after the status is reconnecting', async () => {
    // Arrange
    const { connection, hub } = setup();
    await connection.start();
    const seen: string[] = [];
    connection.onReconnecting(() => seen.push(connection.status()));

    // Act
    hub.reconnecting();

    // Assert
    expect(seen).toEqual(['reconnecting']);
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
    const { connection, hub } = setup();
    const received: number[] = [];
    connection.on<number>('Tick', (n) => received.push(n));
    await connection.start();
    hub.reconnecting();
    hub.reconnected();

    // Act
    hub.emit('Tick', 7);

    // Assert
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
});
