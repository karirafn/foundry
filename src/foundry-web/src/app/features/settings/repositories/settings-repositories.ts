import { Component, ChangeDetectionStrategy } from '@angular/core';

@Component({
  selector: 'fd-settings-repositories',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="repositories-placeholder">
      <section class="repositories-placeholder__section">
        <h2 class="repositories-placeholder__heading">Repositories</h2>
        <p class="repositories-placeholder__subtext">Repository management is coming soon.</p>
      </section>
    </div>
  `,
  styleUrl: './settings-repositories.scss',
})
export class SettingsRepositoriesComponent {}
