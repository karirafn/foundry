import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { Observable, Subject } from 'rxjs';
import {
  CREDITS_NOTIFICATION_CATEGORY,
  DISPATCH_NOTIFICATION_CATEGORY,
  DOCKER_NOTIFICATION_CATEGORY,
  IMAGE_BUILD_NOTIFICATION_CATEGORY,
  SystemNotification,
} from '../models/system-notification.model';
import { DOCKER_UNAVAILABLE_MESSAGE } from '../models/system-status.model';
import { LoginSessionUpdate } from '../models/settings.model';
import { UsageSnapshot } from '../models/usage.model';
import { HubConnections, SharedHubConnection } from './hub-connection';

const RELOAD_TRIGGER_CATEGORIES: ReadonlySet<string> = new Set([
  DISPATCH_NOTIFICATION_CATEGORY,
  IMAGE_BUILD_NOTIFICATION_CATEGORY,
]);

@Injectable({ providedIn: 'root' })
export class SystemSignalRService {
  private readonly _hub: SharedHubConnection = inject(HubConnections).get('system');

  private readonly _notifications: WritableSignal<SystemNotification[]> = signal([]);

  readonly notifications: Signal<SystemNotification[]> = this._notifications.asReadonly();

  private readonly _reconnected = new Subject<void>();
  readonly reconnected: Observable<void> = this._reconnected.asObservable();

  private readonly _reloadTrigger = new Subject<void>();
  readonly reloadTrigger: Observable<void> = this._reloadTrigger.asObservable();

  private readonly _loginSessionUpdate = new Subject<LoginSessionUpdate>();
  readonly loginSessionUpdate: Observable<LoginSessionUpdate> = this._loginSessionUpdate.asObservable();

  private readonly _creditsNotification = new Subject<SystemNotification>();
  readonly creditsNotification: Observable<SystemNotification> = this._creditsNotification.asObservable();

  private readonly _usageSnapshotUpdated = new Subject<UsageSnapshot | null>();
  readonly usageSnapshotUpdated: Observable<UsageSnapshot | null> = this._usageSnapshotUpdated.asObservable();

  constructor() {
    this._hub.on('SystemNotificationReceived', (notification: SystemNotification) => {
      if (RELOAD_TRIGGER_CATEGORIES.has(notification.category)) {
        this._reloadTrigger.next();
      }

      if (notification.category === CREDITS_NOTIFICATION_CATEGORY) {
        this._creditsNotification.next(notification);
      }

      this._applyNotification(notification);
    });

    this._hub.on('LoginSessionUpdated', (update: LoginSessionUpdate) => {
      this._loginSessionUpdate.next(update);
    });

    this._hub.on('UsageSnapshotUpdated', (snapshot: UsageSnapshot | null) => {
      this._usageSnapshotUpdated.next(snapshot);
    });

    this._hub.onConnected(() => {
      this._reconnected.next();
    });

    this._hub.start().catch(() => {
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
