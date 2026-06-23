import { Component, Signal, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SystemBannerComponent } from './shared/components/system-banner/system-banner';
import { ForgeOverlayComponent } from './shared/components/forge-overlay/forge-overlay';
import { SettingsService } from './features/settings/settings.service';

@Component({
  selector: 'fd-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, SystemBannerComponent, ForgeOverlayComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly _settingsService = inject(SettingsService);

  readonly overlayBlocking: Signal<boolean> = this._settingsService.isColdBuildBlocking;
}
