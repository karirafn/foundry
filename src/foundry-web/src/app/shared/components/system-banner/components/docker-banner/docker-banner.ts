import { Component, Signal, computed, inject } from '@angular/core';
import { SystemSignalRService } from '../../../../../core/services/system-signalr.service';
import { SystemStatusService } from '../../../../../core/services/system-status.service';
import { DOCKER_NOTIFICATION_CATEGORY } from '../../../../../core/models/system-notification.model';

@Component({
  selector: 'fd-docker-banner',
  standalone: true,
  imports: [],
  templateUrl: './docker-banner.html',
  styleUrl: './docker-banner.scss',
})
export class DockerBannerComponent {
  private readonly _systemSignalR = inject(SystemSignalRService);
  readonly systemStatusService = inject(SystemStatusService);

  readonly isDockerBannerVisible: Signal<boolean> = computed(() =>
    this._systemSignalR.notifications().some((n) => n.category === DOCKER_NOTIFICATION_CATEGORY)
  );
}
