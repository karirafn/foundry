import { Injectable, signal, WritableSignal } from '@angular/core';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';

export type ConnectionStatus = 'connected' | 'reconnecting' | 'disconnected';

@Injectable({ providedIn: 'root' })
export class IssueSignalRService {
  readonly connectionStatus: WritableSignal<ConnectionStatus> = signal('disconnected');

  private readonly _connection: HubConnection;
  private _reconnectedCallbacks: Array<() => void> = [];

  constructor() {
    this._connection = new HubConnectionBuilder()
      .withUrl('/hubs/issues')
      .withAutomaticReconnect()
      .build();

    this._connection.onreconnecting(() => {
      this.connectionStatus.set('reconnecting');
    });

    this._connection.onreconnected(() => {
      this.connectionStatus.set('connected');
      for (const callback of this._reconnectedCallbacks) {
        callback();
      }
    });

    this._connection.onclose(() => {
      this.connectionStatus.set('disconnected');
    });

    this.init();
  }

  on<T>(methodName: string, callback: (data: T) => void): void {
    this._connection.on(methodName, callback);
  }

  onReconnected(callback: () => void): void {
    this._reconnectedCallbacks.push(callback);
  }

  private init(): void {
    this._connection
      .start()
      .then(() => {
        this.connectionStatus.set('connected');
      })
      .catch(() => {
        this.connectionStatus.set('disconnected');
      });
  }
}
