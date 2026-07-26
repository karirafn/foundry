import { Component } from '@angular/core';
import { SystemNotificationsComponent } from './components/system-notifications/system-notifications';
import { ImageBuildBannerComponent } from './components/image-build-banner/image-build-banner';
import { DispatchBannerComponent } from './components/dispatch-banner/dispatch-banner';
import { DockerBannerComponent } from './components/docker-banner/docker-banner';

@Component({
  selector: 'fd-system-banner',
  standalone: true,
  imports: [SystemNotificationsComponent, DockerBannerComponent, ImageBuildBannerComponent, DispatchBannerComponent],
  templateUrl: './system-banner.html',
  styleUrl: './system-banner.scss',
})
export class SystemBannerComponent {}
