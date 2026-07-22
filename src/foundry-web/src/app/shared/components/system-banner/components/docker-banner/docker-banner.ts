import { ChangeDetectionStrategy, Component, Signal, computed, inject } from '@angular/core';
import { SystemSignalRService } from '../../../../../core/services/system-signalr.service';
import { SystemStatusService } from '../../../../../core/services/system-status.service';
import { DOCKER_NOTIFICATION_CATEGORY } from '../../../../../core/models/system-notification.model';
import { DOCKER_UNAVAILABLE_MESSAGE } from '../../../../../core/models/system-status.model';

@Component({
  selector: 'fd-docker-banner',
  standalone: true,
  imports: [],
  templateUrl: './docker-banner.html',
  styleUrl: './docker-banner.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DockerBannerComponent {
  private readonly _systemSignalR = inject(SystemSignalRService);
  private readonly _systemStatusService = inject(SystemStatusService);

  protected readonly dockerUnavailableMessage = DOCKER_UNAVAILABLE_MESSAGE;

  readonly isDockerBannerVisible: Signal<boolean> = computed(() =>
    this._systemSignalR.notifications().some((n) => n.category === DOCKER_NOTIFICATION_CATEGORY)
  );
}
