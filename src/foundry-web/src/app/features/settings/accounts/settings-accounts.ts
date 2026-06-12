import { Component, ChangeDetectionStrategy } from '@angular/core';

@Component({
  selector: 'fd-settings-accounts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p class="settings-accounts__placeholder">Accounts settings</p>`,
})
export class SettingsAccountsComponent {}
