import { Component, ChangeDetectionStrategy } from '@angular/core';

@Component({
  selector: 'fd-settings-repositories',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p class="settings-repositories__placeholder">Repositories settings</p>`,
})
export class SettingsRepositoriesComponent {}
