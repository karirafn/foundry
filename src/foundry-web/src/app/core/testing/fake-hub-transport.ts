import { Provider } from '@angular/core';
import { IStreamResult, IStreamSubscriber, ISubscription } from '@microsoft/signalr';
import { HUB_TRANSPORT_FACTORY, HubName, HubTransport } from '../services/hub-connection';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type HubHandler = (...args: any[]) => void;

/** A stream a spec drives by hand: it records its subscriber so the spec can push lines. */
export class FakeHubStream<T> implements IStreamResult<T> {
  subscriber: IStreamSubscriber<T> | null = null;
  disposed = false;

  constructor(readonly methodName: string, readonly args: readonly unknown[]) {}

  subscribe(subscriber: IStreamSubscriber<T>): ISubscription<T> {
    this.subscriber = subscriber;
    return { dispose: () => { this.disposed = true; } };
  }
}

/** Stands in for a SignalR connection so a spec can push hub messages and connection changes. */
export class FakeHubTransport implements HubTransport {
  readonly streams: FakeHubStream<unknown>[] = [];
  startCount = 0;
  /** What the next `start()` returns; replace it to make the start fail or stay pending. */
  startResult: () => Promise<void> = () => Promise.resolve();

  private readonly _handlers = new Map<string, HubHandler[]>();
  private readonly _reconnecting: Array<() => void> = [];
  private readonly _reconnected: Array<() => void> = [];
  private readonly _closed: Array<() => void> = [];

  on(methodName: string, method: HubHandler): void {
    this._handlers.set(methodName, [...(this._handlers.get(methodName) ?? []), method]);
  }

  off(methodName: string, method: HubHandler): void {
    this._handlers.set(methodName, (this._handlers.get(methodName) ?? []).filter((h) => h !== method));
  }

  stream<T>(methodName: string, ...args: unknown[]): IStreamResult<T> {
    const stream = new FakeHubStream<T>(methodName, args);
    this.streams.push(stream as FakeHubStream<unknown>);
    return stream;
  }

  start(): Promise<void> {
    this.startCount++;
    return this.startResult();
  }

  onreconnecting(callback: () => void): void {
    this._reconnecting.push(callback);
  }

  onreconnected(callback: () => void): void {
    this._reconnected.push(callback);
  }

  onclose(callback: () => void): void {
    this._closed.push(callback);
  }

  hasHandler(methodName: string): boolean {
    return (this._handlers.get(methodName) ?? []).length > 0;
  }

  /** Delivers a hub message to every handler registered for it. */
  emit(methodName: string, ...args: unknown[]): void {
    for (const handler of this._handlers.get(methodName) ?? []) {
      handler(...args);
    }
  }

  /** The established connection drops and SignalR starts reconnecting. */
  reconnecting(): void {
    this._reconnecting.forEach((callback) => callback());
  }

  /** SignalR re-establishes a dropped connection. */
  reconnected(): void {
    this._reconnected.forEach((callback) => callback());
  }

  /** The connection closes for good. */
  close(): void {
    this._closed.forEach((callback) => callback());
  }
}

/** Fakes every hub; hubs not given here get a fresh FakeHubTransport. */
export function provideFakeHubs(hubs: Partial<Record<HubName, FakeHubTransport>> = {}): Provider {
  return {
    provide: HUB_TRANSPORT_FACTORY,
    useValue: (hub: HubName): HubTransport => hubs[hub] ?? new FakeHubTransport(),
  };
}
