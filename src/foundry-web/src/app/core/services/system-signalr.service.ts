import { Injectable, InjectionToken, Signal, WritableSignal, inject, signal } from '@angular/core';
import { Observable, Subject } from 'rxjs';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import {
  DISPATCH_NOTIFICATION_CATEGORY,
  DOCKER_NOTIFICATION_CATEGORY,
  IMAGE_BUILD_NOTIFICATION_CATEGORY,
  SystemNotification,
} from '../models/system-notification.model';
import { DOCKER_UNAVAILABLE_MESSAGE } from '../models/system-status.model';
import { LoginSessionUpdate } from '../models/settings.model';

const RELOAD_TRIGGER_CATEGORIES: ReadonlySet<string> = new Set([
  DISPATCH_NOTIFICATION_CATEGORY,
  IMAGE_BUILD_NOTIFICATION_CATEGORY,
]);

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

  private readonly _reloadTrigger = new Subject<void>();
  readonly reloadTrigger: Observable<void> = this._reloadTrigger.asObservable();

  private readonly _loginSessionUpdate = new Subject<LoginSessionUpdate>();
  readonly loginSessionUpdate: Observable<LoginSessionUpdate> = this._loginSessionUpdate.asObservable();

  constructor() {
    const hub = this._hubFactory();

    hub.on('SystemNotificationReceived', (notification: SystemNotification) => {
      if (RELOAD_TRIGGER_CATEGORIES.has(notification.category)) {
        this._reloadTrigger.next();
      }

      this._applyNotification(notification);
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

  applyDockerAvailability(available: boolean): void {
    const notification: SystemNotification = {
      category: DOCKER_NOTIFICATION_CATEGORY,
      isActive: !available,
      message: DOCKER_UNAVAILABLE_MESSAGE,
    };
    this._applyNotification(notification);
  }

  private _applyNotification(notification: SystemNotification): void {
    this._notifications.update((current) => {
      const filtered = current.filter((n) => n.category !== notification.category);
      return notification.isActive ? [...filtered, notification] : filtered;
    });
  }
}
