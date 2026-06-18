import { Component, DestroyRef, Signal, computed, effect, inject, signal } from '@angular/core';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { SystemNotification } from '../../../core/models/system-notification.model';
import { DispatchService } from '../../../core/services/dispatch.service';
import { SettingsService } from '../../../features/settings/settings.service';

const DISPATCH_NOTIFICATION_CATEGORY = 'dispatch';
const COUNTDOWN_INTERVAL_MS = 1000;

@Component({
  selector: 'fd-system-banner',
  standalone: true,
  templateUrl: './system-banner.html',
  styleUrl: './system-banner.scss',
})
export class SystemBannerComponent {
  private readonly _systemSignalR = inject(SystemSignalRService);
  private readonly _dispatchService = inject(DispatchService);
  private readonly _settingsService = inject(SettingsService);
  private readonly _destroyRef = inject(DestroyRef);

  private readonly _tickSignal = signal(0);

  readonly activeNotifications: Signal<SystemNotification[]> = this._systemSignalR.notifications;

  readonly isDispatchBannerVisible: Signal<boolean> = computed(
    () => this._dispatchService.isDispatchPaused() || this._dispatchService.usageLimitResetsAt() !== null
  );

  readonly isResuming: Signal<boolean> = this._dispatchService.resuming;

  readonly countdownText: Signal<string | null> = computed(() => {
    this._tickSignal();
    const resetsAt = this._dispatchService.usageLimitResetsAt();
    if (resetsAt === null) {
      return null;
    }
    return this._formatCountdown(new Date(resetsAt).getTime() - Date.now());
  });

  constructor() {
    effect(() => {
      const notifications = this._systemSignalR.notifications();
      const hasDispatch = notifications.some((n) => n.category === DISPATCH_NOTIFICATION_CATEGORY);
      if (hasDispatch) {
        this._settingsService.loadSettings();
      }
    });

    const intervalId = setInterval(() => {
      if (this._dispatchService.usageLimitResetsAt() !== null) {
        this._tickSignal.update((n) => n + 1);
      }
    }, COUNTDOWN_INTERVAL_MS);

    this._destroyRef.onDestroy(() => clearInterval(intervalId));
  }

  resumeDispatch(): void {
    this._dispatchService.resumeDispatch();
  }

  private _formatCountdown(remainingMs: number): string {
    if (remainingMs <= 0) {
      return 'momentarily';
    }

    const totalSeconds = Math.ceil(remainingMs / 1000);
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;

    if (hours >= 1) {
      return `${hours}h ${minutes}m`;
    }

    if (totalSeconds >= 60) {
      return `${minutes}m ${seconds}s`;
    }

    return `${seconds}s`;
  }
}
