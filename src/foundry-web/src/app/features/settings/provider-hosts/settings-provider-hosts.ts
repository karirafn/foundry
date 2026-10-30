import {
  ChangeDetectionStrategy,
  Component,
  Signal,
  WritableSignal,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../../../core/services/settings.service';
import { UnsavedChanges } from '../guards/unsaved-changes.guard';
import { RateBudgetPanelComponent } from './rate-budget-panel/rate-budget-panel';

const RATE_BUDGET_FLOOR = 500;

@Component({
  selector: 'fd-settings-provider-hosts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RateBudgetPanelComponent],
  template: `
    <div class="general-settings">
      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Provider Rate Budget</h2>
        <p class="general-settings__section-description">
          Current provider API request headroom. GitHub budgets show a health status relative to the
          configured floor ({{ RATE_BUDGET_FLOOR }} remaining). GitLab is recorded for visibility only — no floor is enforced.
        </p>
        <fd-rate-budget-panel />
      </section>

      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Provider Hosts</h2>
        <p class="general-settings__section-description">
          Allowlist of self-hosted GitHub Enterprise and GitLab hostnames that workers may reach. Repositories on any other host are rejected before a token is sent.
        </p>

        <div class="general-settings__hosts-form">
          <div class="general-settings__field">
            <label class="general-settings__field-label" for="allowedProviderHosts">Allowed hostnames</label>
            <textarea
              class="general-settings__textarea"
              id="allowedProviderHosts"
              rows="6"
              spellcheck="false"
              autocapitalize="off"
              autocorrect="off"
              aria-describedby="allowed-provider-hosts-hint allowed-provider-hosts-error"
              placeholder="git.example.com&#10;gitlab.internal.corp"
              [ngModel]="_allowedHostsValue()"
              (ngModelChange)="_allowedHostsValue.set($event)"
              [attr.aria-invalid]="!!settingsService.saveHostsError() || null"
            ></textarea>
            <span id="allowed-provider-hosts-hint" class="general-settings__field-hint">One bare hostname per line or comma-separated, e.g. git.example.com — no scheme, port, or path. Maximum 50 hostnames. Leave empty to allow only the public providers.</span>
          </div>

          <div id="allowed-provider-hosts-error" role="alert" class="general-settings__save-error">{{ settingsService.saveHostsError() ?? '' }}</div>

          <div role="status" class="general-settings__save-success">{{ settingsService.saveHostsSuccess() ? 'Provider hosts saved successfully' : '' }}</div>

          <button
            class="general-settings__save-btn"
            type="button"
            [disabled]="settingsService.savingHosts()"
            (click)="saveHosts()"
          >{{ settingsService.savingHosts() ? 'Saving...' : 'Save' }}</button>
        </div>
      </section>
    </div>
  `,
  styleUrl: './settings-provider-hosts.scss',
})
export class SettingsProviderHostsComponent implements UnsavedChanges {
  protected readonly settingsService = inject(SettingsService);

  protected readonly RATE_BUDGET_FLOOR = RATE_BUDGET_FLOOR;

  protected readonly _allowedHostsValue: WritableSignal<string> = signal('');
  private _hostsInitialized = false;

  private readonly _isHostsDirty: Signal<boolean> = computed(() => {
    const hosts = this.settingsService.allowedProviderHosts();
    if (this.settingsService.settings() === null) {
      return false;
    }
    return this._allowedHostsValue() !== hosts.join('\n');
  });

  constructor() {
    effect(() => {
      const hosts = this.settingsService.allowedProviderHosts();
      if (this.settingsService.settings() !== null && !this._hostsInitialized) {
        this._hostsInitialized = true;
        this._allowedHostsValue.set(hosts.join('\n'));
      }
    });
  }

  hasUnsavedChanges(): boolean {
    return this._isHostsDirty();
  }

  saveHosts(): void {
    const hosts = this._allowedHostsValue()
      .split(/[\n,]+/)
      .map((h) => h.trim())
      .filter((h) => h.length > 0);
    this.settingsService.updateAllowedProviderHosts(hosts);
  }
}
