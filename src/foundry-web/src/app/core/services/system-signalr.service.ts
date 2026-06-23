import { Injectable, InjectionToken, Signal, WritableSignal, computed, inject, signal } from '@angular/core';
import { Observable, Subject } from 'rxjs';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { SystemNotification } from '../models/system-notification.model';

export interface SystemHub {
  on(methodName: string, callback: (notification: SystemNotification) => void): void;
  onReconnected(callback: () => void): void;
  start(): Promise<void>;
}

function buildSystemHub(): SystemHub {
  const conn: HubConnection = new HubConnectionBuilder()
    .withUrl('/hubs/system')
    .withAutomaticReconnect()
    .build();

  return {
    on: (methodName, callback) => conn.on(methodName, callback),
    onReconnected: (callback) => conn.onreconnected(callback),
    start: () => conn.start(),
  };
}

export const SYSTEM_HUB_FACTORY = new InjectionToken<() => SystemHub>(
  'SystemHubFactory',
  { providedIn: 'root', factory: () => buildSystemHub }
);

@Injectable({ providedIn: 'root' })
export class SystemSignalRService {
  private readonly _hubFactory = inject(SYSTEM_HUB_FACTORY);

  private readonly _notifications: WritableSignal<SystemNotification[]> = signal([]);

  readonly notifications: Signal<SystemNotification[]> = computed(() => this._notifications());

  private readonly _reconnected = new Subject<void>();
  readonly reconnected: Observable<void> = this._reconnected.asObservable();

  constructor() {
    const hub = this._hubFactory();

    hub.on('SystemNotificationReceived', (notification: SystemNotification) => {
      this._notifications.update((current) => {
        const filtered = current.filter((n) => n.category !== notification.category);
        return notification.isActive ? [...filtered, notification] : filtered;
      });
    });

    hub.onReconnected(() => {
      this._reconnected.next();
    });

    hub.start().catch(() => {
      console.warn('[SystemSignalRService] Failed to connect to /hubs/system');
    });
  }
}
