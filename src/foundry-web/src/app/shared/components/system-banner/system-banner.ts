import { Component } from '@angular/core';
import { SystemNotificationsComponent } from './components/system-notifications/system-notifications';
import { ImageBuildBannerComponent } from './components/image-build-banner/image-build-banner';
import { DispatchBannerComponent } from './components/dispatch-banner/dispatch-banner';
import { DockerBannerComponent } from './components/docker-banner/docker-banner';
import { CreditsBannerComponent } from './components/credits-banner/credits-banner';
import { HostPauseBannerComponent } from './components/host-pause-banner/host-pause-banner';
import { AccountTokenBannerComponent } from './components/account-token-banner/account-token-banner';
import { AccountTokenExpiryBannerComponent } from './components/account-token-expiry-banner/account-token-expiry-banner';

@Component({
  selector: 'fd-system-banner',
  standalone: true,
  imports: [SystemNotificationsComponent, DockerBannerComponent, ImageBuildBannerComponent, CreditsBannerComponent, DispatchBannerComponent, AccountTokenBannerComponent, HostPauseBannerComponent, AccountTokenExpiryBannerComponent],
  templateUrl: './system-banner.html',
  styleUrl: './system-banner.scss',
})
export class SystemBannerComponent {}
