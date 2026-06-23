import { Component, Signal, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SystemBannerComponent } from './shared/components/system-banner/system-banner';
import { ForgeOverlayComponent } from './shared/components/forge-overlay/forge-overlay';
import { SettingsService } from './features/settings/settings.service';
import { AccountService } from './features/settings/accounts/account.service';

@Component({
  selector: 'fd-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, SystemBannerComponent, ForgeOverlayComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly _settingsService = inject(SettingsService);
  private readonly _accountService = inject(AccountService);

  readonly overlayBlocking: Signal<boolean> = computed(
    () => this._accountService.accounts().length > 0 && !this._settingsService.hasUsableImage()
  );
}
