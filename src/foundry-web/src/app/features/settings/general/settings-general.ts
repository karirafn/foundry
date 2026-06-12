import { Component, ChangeDetectionStrategy } from '@angular/core';

@Component({
  selector: 'fd-settings-general',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p class="settings-general__placeholder">General settings</p>`,
})
export class SettingsGeneralComponent {}
