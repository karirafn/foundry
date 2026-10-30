import {
  Component,
  ChangeDetectionStrategy,
  WritableSignal,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../../../core/services/settings.service';
import { RateBudgetPanelComponent } from './rate-budget-panel/rate-budget-panel';
import { UpdatePromptTemplatesRequest } from '../../../core/models/settings.model';

const RATE_BUDGET_FLOOR = 500;

@Component({
  selector: 'fd-settings-general',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RateBudgetPanelComponent],
  template: `
    <div class="general-settings">
      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Worker Prompts</h2>
        <p class="general-settings__section-description">
          Customize the system and worker prompts sent to Claude Code containers. Use &#123;issueNumber&#125;, &#123;issueContent&#125;, and &#123;branchNamingInstruction&#125; as template variables.
        </p>

        <div class="general-settings__prompts-form">
          <div class="general-settings__field">
            <label class="general-settings__field-label" for="systemPromptTemplate">System Prompt Template</label>
            <textarea
              class="general-settings__textarea"
              id="systemPromptTemplate"
              rows="6"
              [ngModel]="_systemPromptValue()"
              (ngModelChange)="_systemPromptValue.set($event)"
              aria-describedby="prompts-error"
            ></textarea>
          </div>

          <div class="general-settings__field">
            <label class="general-settings__field-label" for="workerPromptTemplate">Worker Prompt Template</label>
            <textarea
              class="general-settings__textarea"
              id="workerPromptTemplate"
              rows="4"
              [ngModel]="_workerPromptValue()"
              (ngModelChange)="_workerPromptValue.set($event)"
              aria-describedby="prompts-error"
            ></textarea>
          </div>

          <div id="prompts-error" role="alert" class="general-settings__save-error">{{ settingsService.savePromptsError() ?? '' }}</div>

          <div role="status" class="general-settings__save-success">{{ settingsService.savePromptsSuccess() ? 'Prompt templates saved successfully' : '' }}</div>

          <button
            class="general-settings__save-btn"
            type="button"
            [disabled]="settingsService.savingPrompts()"
            (click)="savePrompts()"
          >{{ settingsService.savingPrompts() ? 'Saving...' : 'Save' }}</button>
        </div>
      </section>

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
  styleUrl: './settings-general.scss',
})
export class SettingsGeneralComponent {
  protected readonly settingsService = inject(SettingsService);

  protected readonly RATE_BUDGET_FLOOR = RATE_BUDGET_FLOOR;

  protected readonly _systemPromptValue: WritableSignal<string> = signal('');
  protected readonly _workerPromptValue: WritableSignal<string> = signal('');
  private _promptsInitialized = false;

  protected readonly _allowedHostsValue: WritableSignal<string> = signal('');
  private _hostsInitialized = false;

  constructor() {
    effect(() => {
      const limits = this.settingsService.workerLimits();
      if (limits !== null && !this._promptsInitialized) {
        this._promptsInitialized = true;
        this._systemPromptValue.set(this.settingsService.systemPromptTemplate() ?? '');
        this._workerPromptValue.set(this.settingsService.workerPromptTemplate() ?? '');
      }
    });

    effect(() => {
      const hosts = this.settingsService.allowedProviderHosts();
      if (this.settingsService.settings() !== null && !this._hostsInitialized) {
        this._hostsInitialized = true;
        this._allowedHostsValue.set(hosts.join('\n'));
      }
    });
  }

  savePrompts(): void {
    const request: UpdatePromptTemplatesRequest = {
      systemPromptTemplate: this._systemPromptValue() || null,
      workerPromptTemplate: this._workerPromptValue() || null,
    };
    this.settingsService.updatePromptTemplates(request);
  }

  saveHosts(): void {
    const hosts = this._allowedHostsValue()
      .split(/[\n,]+/)
      .map((h) => h.trim())
      .filter((h) => h.length > 0);
    this.settingsService.updateAllowedProviderHosts(hosts);
  }
}
