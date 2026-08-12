import { ChangeDetectionStrategy, Component, Signal, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SystemBannerComponent } from './shared/components/system-banner/system-banner';
import { ForgeOverlayComponent } from './shared/components/forge-overlay/forge-overlay';
import { ToastHostComponent } from './shared/components/toast-host/toast-host';
import { AccountChipComponent } from './shared/components/account-chip/account-chip';
import { SettingsService } from './core/services/settings.service';
import { AccountService } from './features/settings/accounts/account.service';

@Component({
  selector: 'fd-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, SystemBannerComponent, ForgeOverlayComponent, ToastHostComponent, AccountChipComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  private readonly _settingsService = inject(SettingsService);
  private readonly _accountService = inject(AccountService);

  readonly overlayBlocking: Signal<boolean> = this._settingsService.isColdBuildBlocking;
  protected readonly srAnnouncement: Signal<string> = this._accountService.srAnnouncement;
}
