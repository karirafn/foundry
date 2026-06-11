import { Component, Signal, inject } from '@angular/core';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { SystemNotification } from '../../../core/models/system-notification.model';

@Component({
  selector: 'fd-system-banner',
  standalone: true,
  templateUrl: './system-banner.html',
  styleUrl: './system-banner.scss',
})
export class SystemBannerComponent {
  private readonly _systemSignalR = inject(SystemSignalRService);
  readonly activeNotifications: Signal<SystemNotification[]> = this._systemSignalR.notifications;
}
