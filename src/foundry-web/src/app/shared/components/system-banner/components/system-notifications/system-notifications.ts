import { Component, InjectionToken, Signal, computed, inject } from '@angular/core';
import { SystemSignalRService } from '../../../../../core/services/system-signalr.service';
import { SystemNotification } from '../../../../../core/models/system-notification.model';

/**
 * Categories whose notifications carry a user-facing message and are rendered by the
 * generic notification bar. Every current backend category (dispatch, docker, image-build,
 * credits) has a dedicated banner or is a reload trigger, so the default is empty and
 * unknown categories fail closed.
 */
export const GENERAL_NOTIFICATION_CATEGORIES = new InjectionToken<ReadonlySet<string>>(
  'GENERAL_NOTIFICATION_CATEGORIES',
  { providedIn: 'root', factory: () => new Set<string>() }
);

@Component({
  selector: 'fd-system-notifications',
  standalone: true,
  imports: [],
  templateUrl: './system-notifications.html',
  styleUrl: './system-notifications.scss',
})
export class SystemNotificationsComponent {
  private readonly _systemSignalR = inject(SystemSignalRService);
  private readonly _allowedCategories = inject(GENERAL_NOTIFICATION_CATEGORIES);

  readonly generalNotifications: Signal<SystemNotification[]> = computed(() =>
    this._systemSignalR
      .notifications()
      .filter((n) => this._allowedCategories.has(n.category) && n.message.trim() !== '')
  );
}
