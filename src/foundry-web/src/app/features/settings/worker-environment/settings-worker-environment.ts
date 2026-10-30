import { ChangeDetectionStrategy, Component } from '@angular/core';
import { UnsavedChanges } from '../guards/unsaved-changes.guard';

@Component({
  selector: 'fd-settings-worker-environment',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="general-settings">
      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Worker environment</h2>
        <p class="general-settings__section-description">
          Settings that control the runtime environment of each worker container
          will appear here. Nothing to configure yet.
        </p>

        <p class="general-settings__section-description">
          Once available, this page will let you manage:
        </p>
        <ul class="general-settings__field-hint">
          <li>Container limits — CPU, memory, and process ceilings for each worker.</li>
          <li>Mounts — host paths and volumes made available inside the worker.</li>
          <li>Host hooks — commands run on the host around a worker's lifecycle.</li>
        </ul>
      </section>
    </div>
  `,
  styleUrl: './settings-worker-environment.scss',
})
export class SettingsWorkerEnvironmentComponent implements UnsavedChanges {
  hasUnsavedChanges(): boolean {
    return false;
  }
}
