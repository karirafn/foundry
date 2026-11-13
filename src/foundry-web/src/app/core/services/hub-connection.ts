import { Injectable, InjectionToken, Signal, WritableSignal, inject, signal } from '@angular/core';
import { HubConnectionBuilder, IStreamResult } from '@microsoft/signalr';

export type ConnectionStatus = 'connected' | 'reconnecting' | 'disconnected';

/** The SignalR hubs the dashboard connects to; each is served at `/hubs/{name}`. */
export type HubName = 'issues' | 'system' | 'workers';

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
  start(): Promise<void>;
  onreconnecting(callback: (error?: Error) => void): void;
  onreconnected(callback: (connectionId?: string) => void): void;
  onclose(callback: (error?: Error) => void): void;
}

function buildSignalRTransport(hub: HubName): HubTransport {
  return new HubConnectionBuilder()
    .withUrl(`/hubs/${hub}`)
    .withAutomaticReconnect()
    .build();
}

export const HUB_TRANSPORT_FACTORY = new InjectionToken<(hub: HubName) => HubTransport>(
  'HubTransportFactory',
  { providedIn: 'root', factory: () => buildSignalRTransport }
);

/**
 * One hub's connection: its status, its handlers and its reconnect notifications. Reconnection is
 * owned here, so a change to how hubs reconnect is made in this one place.
 */
export class SharedHubConnection {
  private readonly _status: WritableSignal<ConnectionStatus> = signal('disconnected');
  readonly status: Signal<ConnectionStatus> = this._status.asReadonly();

  private readonly _reconnectingCallbacks: Array<() => void> = [];
  private readonly _reconnectedCallbacks: Array<() => void> = [];

  constructor(private readonly _transport: HubTransport) {
    _transport.onreconnecting(() => {
      this._status.set('reconnecting');
      for (const callback of this._reconnectingCallbacks) {
        callback();
      }
    });

    _transport.onreconnected(() => {
      this._status.set('connected');
      for (const callback of this._reconnectedCallbacks) {
        callback();
      }
    });

    _transport.onclose(() => {
      this._status.set('disconnected');
    });
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

  /** Starts the connection; a failed start rejects and leaves the status `disconnected`. */
  start(): Promise<void> {
    return this._transport.start().then(
      () => this._status.set('connected'),
      (error: unknown) => {
        this._status.set('disconnected');
        throw error;
      }
    );
  }

  /** Runs after the status turns `reconnecting`, when an established connection drops. */
  onReconnecting(callback: () => void): void {
    this._reconnectingCallbacks.push(callback);
  }

  /** Runs after the status turns `connected` again following a drop. */
  onReconnected(callback: () => void): void {
    this._reconnectedCallbacks.push(callback);
  }
}

/** Hands out the one shared connection per hub. */
@Injectable({ providedIn: 'root' })
export class HubConnections {
  private readonly _transportFactory = inject(HUB_TRANSPORT_FACTORY);
  private readonly _connections = new Map<HubName, SharedHubConnection>();

  get(hub: HubName): SharedHubConnection {
    let connection = this._connections.get(hub);
    if (connection === undefined) {
      connection = new SharedHubConnection(this._transportFactory(hub));
      this._connections.set(hub, connection);
    }
    return connection;
  }
}
