import { DestroyRef, Injectable, InjectionToken, Signal, WritableSignal, inject, signal } from '@angular/core';
import { HubConnectionBuilder, IStreamResult } from '@microsoft/signalr';
import { retryDelayMs } from './retry-schedule';

/**
 * `disconnected` until the first start; `reconnecting` whenever a start has failed or the connection
 * dropped, since the connection then retries until it is back.
 */
export type ConnectionStatus = 'connected' | 'reconnecting' | 'disconnected';

/** The SignalR hubs the dashboard connects to; each is served at `/hubs/{name}`. */
export type HubName = 'issues' | 'system' | 'workers';

export const HUB_NAMES: readonly HubName[] = ['issues', 'system', 'workers'];

/**
 * The part of SignalR's `HubConnection` that a {@link SharedHubConnection} drives. It is the single
 * test seam for every hub: specs replace {@link HUB_TRANSPORT_FACTORY} to fake all three the same way.
 */
export interface HubTransport {
  // SignalR passes hub arguments untyped; SharedHubConnection.on narrows them for its callers.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  on(methodName: string, method: (...args: any[]) => void): void;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  off(methodName: string, method: (...args: any[]) => void): void;
  stream<T>(methodName: string, ...args: unknown[]): IStreamResult<T>;
  invoke<T>(methodName: string, ...args: unknown[]): Promise<T>;
  start(): Promise<void>;
  onclose(callback: (error?: Error) => void): void;
}

// No automatic reconnect: SharedHubConnection owns the retry loop, because HubConnection.start()
// rejects unless the connection is Disconnected, so SignalR's own loop could not be cut short by
// Try now, and it never retries a failed first start.
function buildSignalRTransport(hub: HubName): HubTransport {
  return new HubConnectionBuilder().withUrl(`/hubs/${hub}`).build();
}

export const HUB_TRANSPORT_FACTORY = new InjectionToken<(hub: HubName) => HubTransport>(
  'HubTransportFactory',
  { providedIn: 'root', factory: () => buildSignalRTransport }
);

/**
 * One hub's connection: its status, its handlers and its retry loop. A failed start or a dropped
 * connection is retried on {@link retryDelayMs}'s schedule with no limit, and {@link tryNow} cuts a
 * pending wait short. Handlers live on the transport, so they keep receiving after every reconnect.
 */
export class SharedHubConnection {
  private readonly _status: WritableSignal<ConnectionStatus> = signal('disconnected');
  readonly status: Signal<ConnectionStatus> = this._status.asReadonly();

  private readonly _nextAttemptAt: WritableSignal<number | null> = signal(null);
  /** When the next start attempt is due, in epoch ms; null while connected or attempting. */
  readonly nextAttemptAt: Signal<number | null> = this._nextAttemptAt.asReadonly();

  private readonly _attempting: WritableSignal<boolean> = signal(false);
  /** A start attempt is in flight. */
  readonly attempting: Signal<boolean> = this._attempting.asReadonly();

  private readonly _connectedCallbacks: Array<() => void> = [];
  private _firstStart: Promise<void> | null = null;
  private _failures = 0;
  private _recovering = false;
  private _timer: ReturnType<typeof setTimeout> | null = null;
  private _disposed = false;

  constructor(private readonly _transport: HubTransport) {
    _transport.onclose(() => this._onDropped());
  }

  on<T>(methodName: string, callback: (payload: T) => void): void {
    this._transport.on(methodName, callback);
  }

  off<T>(methodName: string, callback: (payload: T) => void): void {
    this._transport.off(methodName, callback);
  }

  stream<T>(methodName: string, ...args: unknown[]): IStreamResult<T> {
    return this._transport.stream<T>(methodName, ...args);
  }

  invoke<T>(methodName: string, ...args: unknown[]): Promise<T> {
    return this._transport.invoke<T>(methodName, ...args);
  }

  /**
   * Starts the connection once; later calls return the same promise. It rejects when the first
   * attempt fails, and the connection then keeps retrying on its own.
   */
  start(): Promise<void> {
    this._firstStart ??= this._attempt();
    return this._firstStart;
  }

  /** Starts the next attempt now, cancelling the wait; does nothing while connected or attempting. */
  tryNow(): void {
    if (this._firstStart === null || this._status() === 'connected' || this._attempting()) {
      return;
    }
    this._attemptQuietly();
  }

  /**
   * Runs on every connect that ends a time without a connection: a reconnect after a drop, or a
   * first connect that only succeeded on a retry. Events may have been missed, so callers refetch.
   */
  onConnected(callback: () => void): void {
    this._connectedCallbacks.push(callback);
  }

  /** Stops retrying; the connection is no longer used. */
  dispose(): void {
    this._disposed = true;
    this._clearTimer();
  }

  private _attempt(): Promise<void> {
    this._clearTimer();
    this._nextAttemptAt.set(null);
    this._attempting.set(true);
    return this._transport.start().then(
      () => this._onConnected(),
      (error: unknown) => {
        this._onAttemptFailed();
        throw error;
      }
    );
  }

  private _attemptQuietly(): void {
    this._attempt().catch(() => {
      // The failure is already scheduled for a retry; the status shows it.
    });
  }

  private _onConnected(): void {
    this._attempting.set(false);
    this._failures = 0;
    this._status.set('connected');
    if (!this._recovering) {
      return;
    }
    this._recovering = false;
    for (const callback of this._connectedCallbacks) {
      callback();
    }
  }

  private _onAttemptFailed(): void {
    this._attempting.set(false);
    this._recovering = true;
    this._failures += 1;
    this._status.set('reconnecting');
    this._schedule(retryDelayMs(this._failures));
  }

  private _onDropped(): void {
    // A failed attempt is handled by its own rejection.
    if (this._attempting()) {
      return;
    }
    this._recovering = true;
    this._failures = 0;
    this._status.set('reconnecting');
    this._schedule(retryDelayMs(0));
  }

  private _schedule(delayMs: number): void {
    this._clearTimer();
    if (this._disposed) {
      return;
    }
    this._nextAttemptAt.set(Date.now() + delayMs);
    this._timer = setTimeout(() => {
      this._timer = null;
      this._attemptQuietly();
    }, delayMs);
  }

  private _clearTimer(): void {
    if (this._timer !== null) {
      clearTimeout(this._timer);
      this._timer = null;
    }
  }
}

/** Hands out the one shared connection per hub. */
@Injectable({ providedIn: 'root' })
export class HubConnections {
  private readonly _transportFactory = inject(HUB_TRANSPORT_FACTORY);
  private readonly _connections = new Map<HubName, SharedHubConnection>();

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      for (const connection of this._connections.values()) {
        connection.dispose();
      }
    });
  }

  get(hub: HubName): SharedHubConnection {
    let connection = this._connections.get(hub);
    if (connection === undefined) {
      connection = new SharedHubConnection(this._transportFactory(hub));
      this._connections.set(hub, connection);
    }
    return connection;
  }
}
