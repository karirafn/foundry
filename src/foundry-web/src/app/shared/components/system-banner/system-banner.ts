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
  private readonly _destroyRef = inject(DestroyRef);

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

    effect(() => {
      const imageBuild = this.imageBuildNotification();
      if (imageBuild !== null) {
        this._settingsService.setImageBuildStatus(imageBuild.status, imageBuild.logTail);
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
