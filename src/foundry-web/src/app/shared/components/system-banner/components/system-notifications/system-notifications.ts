import { Component, Signal, computed, inject } from '@angular/core';
import { SystemSignalRService } from '../../../../../core/services/system-signalr.service';
import {
  DISPATCH_NOTIFICATION_CATEGORY,
  DOCKER_NOTIFICATION_CATEGORY,
  IMAGE_BUILD_NOTIFICATION_CATEGORY,
  SystemNotification,
} from '../../../../../core/models/system-notification.model';

@Component({
  selector: 'fd-system-notifications',
  standalone: true,
  imports: [],
  templateUrl: './system-notifications.html',
  styleUrl: './system-notifications.scss',
})
export class SystemNotificationsComponent {
  private readonly _systemSignalR = inject(SystemSignalRService);

  readonly generalNotifications: Signal<SystemNotification[]> = computed(() =>
    this._systemSignalR
      .notifications()
      .filter(
        (n) =>
          n.category !== IMAGE_BUILD_NOTIFICATION_CATEGORY &&
          n.category !== DISPATCH_NOTIFICATION_CATEGORY &&
          n.category !== DOCKER_NOTIFICATION_CATEGORY
      )
  );
}
