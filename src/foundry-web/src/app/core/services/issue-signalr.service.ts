import { Injectable, Signal, inject } from '@angular/core';
import { ConnectionStatus, HubConnections, SharedHubConnection } from './hub-connection';

export type { ConnectionStatus } from './hub-connection';

@Injectable({ providedIn: 'root' })
export class IssueSignalRService {
  private readonly _connection: SharedHubConnection = inject(HubConnections).get('issues');

  readonly connectionStatus: Signal<ConnectionStatus> = this._connection.status;

  constructor() {
    this._connection.start().catch(() => {
      // The status reads 'reconnecting' and the connection retries; the connection gate shows it.
    });
  }

  on<T>(methodName: string, callback: (data: T) => void): void {
    this._connection.on(methodName, callback);
  }

  /** Removes a handler registered with `on`, for a consumer that lives shorter than the app. */
  off<T>(methodName: string, callback: (data: T) => void): void {
    this._connection.off(methodName, callback);
  }

  onReconnected(callback: () => void): void {
    this._connection.onConnected(callback);
  }
}
