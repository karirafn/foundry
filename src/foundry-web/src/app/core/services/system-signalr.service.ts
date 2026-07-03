import { Injectable, InjectionToken, Signal, WritableSignal, inject, signal } from '@angular/core';
import { Observable, Subject } from 'rxjs';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { DISPATCH_NOTIFICATION_CATEGORY, SystemNotification } from '../models/system-notification.model';
import { LoginSessionUpdate } from '../../features/settings/settings.model';

export interface SystemHub {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  on(methodName: string, callback: (...args: any[]) => void): void;
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

  readonly notifications: Signal<SystemNotification[]> = this._notifications.asReadonly();

  private readonly _reconnected = new Subject<void>();
  readonly reconnected: Observable<void> = this._reconnected.asObservable();

  private readonly _dispatchStateChanged = new Subject<void>();
  readonly dispatchStateChanged: Observable<void> = this._dispatchStateChanged.asObservable();

  private readonly _loginSessionUpdate = new Subject<LoginSessionUpdate>();
  readonly loginSessionUpdate: Observable<LoginSessionUpdate> = this._loginSessionUpdate.asObservable();

  constructor() {
    const hub = this._hubFactory();

    hub.on('SystemNotificationReceived', (notification: SystemNotification) => {
      if (notification.category === DISPATCH_NOTIFICATION_CATEGORY) {
        this._dispatchStateChanged.next();
      }

      this._notifications.update((current) => {
        const filtered = current.filter((n) => n.category !== notification.category);
        return notification.isActive ? [...filtered, notification] : filtered;
      });
    });

    hub.on('LoginSessionUpdated', (update: LoginSessionUpdate) => {
      this._loginSessionUpdate.next(update);
    });

    hub.onReconnected(() => {
      this._reconnected.next();
    });

    hub.start().catch(() => {
      console.warn('[SystemSignalRService] Failed to connect to /hubs/system');
    });
  }
}
