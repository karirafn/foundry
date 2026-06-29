import { Component, Signal, computed, effect, inject } from '@angular/core';
import { SlicePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { SystemSignalRService } from '../../../../../core/services/system-signalr.service';
import { IMAGE_BUILD_NOTIFICATION_CATEGORY } from '../../../../../core/models/system-notification.model';
import { SettingsService } from '../../../../../features/settings/settings.service';
import { ImageBuildStatus } from '../../../../../features/settings/settings.model';

const IMAGE_BUILD_MESSAGE_SEPARATOR = '|';

interface ParsedImageBuildNotification {
  status: ImageBuildStatus;
  logTail: string | null;
}

@Component({
  selector: 'fd-image-build-banner',
  standalone: true,
  imports: [RouterLink, SlicePipe],
  templateUrl: './image-build-banner.html',
  styleUrl: './image-build-banner.scss',
})
export class ImageBuildBannerComponent {
  private readonly _systemSignalR = inject(SystemSignalRService);
  private readonly _settingsService = inject(SettingsService);

  readonly imageBuildNotification: Signal<ParsedImageBuildNotification | null> = computed(() => {
    const notification = this._systemSignalR.notifications().find(
      (n) => n.category === IMAGE_BUILD_NOTIFICATION_CATEGORY
    );
    if (!notification) {
      return null;
    }
    return this._parseImageBuildMessage(notification.message);
  });

  constructor() {
    effect(() => {
      const imageBuild = this.imageBuildNotification();
      if (imageBuild !== null) {
        this._settingsService.setImageBuildStatus(imageBuild.status, imageBuild.logTail);
      }
    });
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
}
