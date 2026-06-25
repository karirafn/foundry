import { Component, DestroyRef, Signal, computed, effect, inject, signal } from '@angular/core';
import { SlicePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import {
  DISPATCH_NOTIFICATION_CATEGORY,
  IMAGE_BUILD_NOTIFICATION_CATEGORY,
  SystemNotification,
} from '../../../core/models/system-notification.model';
import { DispatchService } from '../../../core/services/dispatch.service';
import { SettingsService } from '../../../features/settings/settings.service';
import { ImageBuildStatus } from '../../../features/settings/settings.model';
import { ToastService } from '../../../core/services/toast.service';

const USAGE_LIMIT_RESET_MESSAGE = 'Usage limit reset';

const COUNTDOWN_INTERVAL_MS = 1000;
const IMAGE_BUILD_MESSAGE_SEPARATOR = '|';

interface ParsedImageBuildNotification {
  status: ImageBuildStatus;
  logTail: string | null;
}

@Component({
  selector: 'fd-system-banner',
  standalone: true,
  imports: [RouterLink, SlicePipe],
  templateUrl: './system-banner.html',
  styleUrl: './system-banner.scss',
})
export class SystemBannerComponent {
  private readonly _systemSignalR = inject(SystemSignalRService);
  private readonly _dispatchService = inject(DispatchService);
  private readonly _settingsService = inject(SettingsService);
  private readonly _toastService = inject(ToastService);
  private readonly _destroyRef = inject(DestroyRef);

  private _wasCountingDown = false;

  private readonly _tickSignal = signal(0);

  readonly generalNotifications: Signal<SystemNotification[]> = computed(() =>
    this._systemSignalR.notifications().filter((n) => n.category !== IMAGE_BUILD_NOTIFICATION_CATEGORY)
  );

  readonly imageBuildNotification: Signal<ParsedImageBuildNotification | null> = computed(() => {
    const notification = this._systemSignalR.notifications().find(
      (n) => n.category === IMAGE_BUILD_NOTIFICATION_CATEGORY
    );
    if (!notification) {
      return null;
    }
    return this._parseImageBuildMessage(notification.message);
  });

  readonly remainingMs: Signal<number | null> = computed(() => {
    this._tickSignal();
    const resetsAt = this._dispatchService.usageLimitResetsAt();
    if (resetsAt === null) {
      return null;
    }
    return new Date(resetsAt).getTime() - Date.now();
  });

  readonly isUsageLimitActive: Signal<boolean> = computed(() => {
    const ms = this.remainingMs();
    return ms !== null && ms > 0;
  });

  readonly isDispatchBannerVisible: Signal<boolean> = computed(
    () => this._dispatchService.isDispatchPaused() || this.isUsageLimitActive()
  );

  readonly countdownText: Signal<string | null> = computed(() => {
    const ms = this.remainingMs();
    if (ms === null || ms <= 0) {
      return null;
    }
    return this._formatCountdown(ms);
  });

  constructor() {
    effect(() => {
      const notifications = this._systemSignalR.notifications();
      const hasDispatch = notifications.some((n) => n.category === DISPATCH_NOTIFICATION_CATEGORY);
      if (hasDispatch) {
        this._settingsService.loadSettings();
      }
    });

    effect(() => {
      const imageBuild = this.imageBuildNotification();
      if (imageBuild !== null) {
        this._settingsService.setImageBuildStatus(imageBuild.status, imageBuild.logTail);
      }
    });

    effect(() => {
      const current = this.remainingMs();
      const resetsAt = this._dispatchService.usageLimitResetsAt();

      if (current === null || resetsAt === null) {
        this._wasCountingDown = false;
        return;
      }

      if (current > 0) {
        this._wasCountingDown = true;
        return;
      }

      // current <= 0 and resetsAt is non-null
      if (this._wasCountingDown) {
        this._toastService.show(USAGE_LIMIT_RESET_MESSAGE);
        this._wasCountingDown = false;
      }
    });

    const intervalId = setInterval(() => {
      if (this._dispatchService.usageLimitResetsAt() !== null) {
        this._tickSignal.update((n) => n + 1);
      }
    }, COUNTDOWN_INTERVAL_MS);

    this._destroyRef.onDestroy(() => clearInterval(intervalId));
  }

  retryImageBuild(): void {
    this._settingsService.retryImageBuild();
  }

  private _parseImageBuildMessage(message: string): ParsedImageBuildNotification {
    const separatorIndex = message.indexOf(IMAGE_BUILD_MESSAGE_SEPARATOR);
    if (separatorIndex === -1) {
      return { status: 'Idle', logTail: null };
    }
    const statusPart = message.slice(0, separatorIndex) as ImageBuildStatus;
    const logPart = message.slice(separatorIndex + 1);
    return {
      status: statusPart,
      logTail: logPart.length > 0 ? logPart : null,
    };
  }

  private _formatCountdown(remainingMs: number): string {
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
