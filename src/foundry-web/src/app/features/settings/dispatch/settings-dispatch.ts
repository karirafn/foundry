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

const MAX_CONCURRENT_MIN = 1;
const MAX_CONCURRENT_MAX = 20;
const TIMEOUT_MINUTES_MIN = 1;
const TIMEOUT_MINUTES_MAX = 1440;
const PROBE_INTERVAL_MIN = 5;
const POLL_INTERVAL_MIN = 5;
const POLL_INTERVAL_MAX = 3600;
const BACKSTOP_INTERVAL_MIN = 5;
const BACKSTOP_INTERVAL_MAX = 1440;
const BACKSTOP_INTERVAL_DEFAULT = 15;
const WEBHOOK_URL_ERROR_CODE = 'Settings.InvalidWebhookPublicBaseUrl';
const BACKSTOP_ERROR_CODE = 'Settings.InvalidBackstopInterval';

/** Client-side mirror of the server rule: optional, otherwise an absolute https URL without query or fragment. */
export function isValidWebhookPublicBaseUrl(value: string): boolean {
  const trimmed = value.trim();
  if (trimmed === '') {
    return true;
  }
  try {
    const url = new URL(trimmed);
    return url.protocol === 'https:' && url.search === '' && url.hash === '' && url.username === '' && url.password === '';
  } catch {
    return false;
  }
}

@Component({
  selector: 'fd-settings-dispatch',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <div class="general-settings">
      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Worker Limits</h2>
        <p class="general-settings__section-description">
          Control the maximum number of concurrent workers and the timeout for each worker run.
        </p>

        <div class="general-settings__limits-form">
          <div class="general-settings__limits-fields">
            <div class="general-settings__field">
              <label class="general-settings__field-label" for="maxConcurrent">Max Concurrent Workers</label>
              <input
                class="general-settings__number-input"
                type="number"
                id="maxConcurrent"
                [min]="MAX_CONCURRENT_MIN"
                [max]="MAX_CONCURRENT_MAX"
                step="1"
                [ngModel]="_maxConcurrentValue()"
                (ngModelChange)="_maxConcurrentValue.set($event)"
                aria-describedby="max-concurrent-hint limits-error"
              />
              <span id="max-concurrent-hint" class="general-settings__field-hint">1–20 workers</span>
            </div>
            <div class="general-settings__field">
              <label class="general-settings__field-label" for="timeoutMinutes">Timeout (minutes)</label>
              <input
                class="general-settings__number-input"
                type="number"
                id="timeoutMinutes"
                [min]="TIMEOUT_MINUTES_MIN"
                [max]="TIMEOUT_MINUTES_MAX"
                step="1"
                [ngModel]="_timeoutMinutesValue()"
                (ngModelChange)="_timeoutMinutesValue.set($event)"
                aria-describedby="timeout-minutes-hint limits-error"
              />
              <span id="timeout-minutes-hint" class="general-settings__field-hint">1–1,440 minutes</span>
            </div>
          </div>

          <div id="limits-error" role="alert" class="general-settings__save-error">{{ settingsService.saveLimitsError() ?? '' }}</div>

          <div role="status" class="general-settings__save-success">{{ settingsService.saveLimitsSuccess() ? 'Worker limits saved successfully' : '' }}</div>

          <button
            class="general-settings__save-btn"
            type="button"
            [disabled]="settingsService.savingLimits() || !isLimitsFormValid()"
            (click)="saveLimits()"
          >{{ settingsService.savingLimits() ? 'Saving...' : 'Save' }}</button>
        </div>
      </section>

      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Dispatch Settings</h2>
        <p class="general-settings__section-description">
          Configure automatic pause and resume behavior for worker dispatch.
        </p>

        <div class="general-settings__dispatch-form">
          <label class="general-settings__checkbox-label" for="autoResume">
            <input
              type="checkbox"
              id="autoResume"
              [ngModel]="_autoResumeValue()"
              (ngModelChange)="_autoResumeValue.set($event)"
              [attr.aria-describedby]="settingsService.saveDispatchError() ? 'dispatch-error' : null"
            />
            Auto-resume when usage limit resets
          </label>

          <div class="general-settings__field">
            <label class="general-settings__field-label" for="probeIntervalMinutes">Credit check interval (minutes)</label>
            <input
              class="general-settings__number-input"
              type="number"
              id="probeIntervalMinutes"
              [min]="PROBE_INTERVAL_MIN"
              step="1"
              [ngModel]="_probeIntervalValue()"
              (ngModelChange)="_probeIntervalValue.set($event)"
              aria-describedby="probe-interval-hint probe-interval-error dispatch-error"
              [attr.aria-invalid]="_probeIntervalValue() < PROBE_INTERVAL_MIN || null"
            />
            <span id="probe-interval-hint" class="general-settings__field-hint">How often Foundry checks whether the Claude account can spend again while dispatch is credit-blocked. Minimum {{ PROBE_INTERVAL_MIN }} minutes.</span>
            <span id="probe-interval-error" class="general-settings__field-error" role="alert">{{ _probeIntervalValue() < PROBE_INTERVAL_MIN ? 'Must be at least ' + PROBE_INTERVAL_MIN + ' minutes' : '' }}</span>
          </div>

          <div class="general-settings__field">
            <label class="general-settings__field-label" for="pollIntervalSeconds">Poll interval (seconds)</label>
            <input
              class="general-settings__number-input"
              type="number"
              id="pollIntervalSeconds"
              [min]="POLL_INTERVAL_MIN"
              [max]="POLL_INTERVAL_MAX"
              step="1"
              [ngModel]="_pollIntervalValue()"
              (ngModelChange)="_pollIntervalValue.set($event)"
              aria-describedby="poll-interval-hint poll-interval-error dispatch-error"
              [attr.aria-invalid]="(_pollIntervalValue() < POLL_INTERVAL_MIN || _pollIntervalValue() > POLL_INTERVAL_MAX) || null"
            />
            <span id="poll-interval-hint" class="general-settings__field-hint">How often Foundry scans repositories for new issues. {{ POLL_INTERVAL_MIN }}–{{ POLL_INTERVAL_MAX }} seconds.</span>
            <span id="poll-interval-error" class="general-settings__field-error" role="alert">{{ _pollIntervalValue() < POLL_INTERVAL_MIN || _pollIntervalValue() > POLL_INTERVAL_MAX ? 'Must be between ' + POLL_INTERVAL_MIN + ' and ' + POLL_INTERVAL_MAX + ' seconds' : '' }}</span>
          </div>

          <div id="dispatch-error" role="alert" class="general-settings__save-error">{{ settingsService.saveDispatchError() ?? '' }}</div>

          <div role="status" class="general-settings__save-success">{{ settingsService.saveDispatchSuccess() ? 'Dispatch settings saved successfully' : '' }}</div>

          <button
            class="general-settings__save-btn"
            type="button"
            [disabled]="settingsService.savingDispatch() || !isDispatchFormValid()"
            (click)="saveDispatch()"
          >{{ settingsService.savingDispatch() ? 'Saving...' : 'Save' }}</button>
        </div>
      </section>

      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Webhooks</h2>
        <p class="general-settings__section-description">
          GitHub webhooks nudge polling so changes show up within seconds. Repositories whose webhook is active poll
          only at the backstop interval. Webhooks also need the host's <code>Webhooks:Port</code> setting; expose that
          port through a tunnel or reverse proxy at the public base URL below.
        </p>

        <div class="general-settings__dispatch-form">
          <div class="general-settings__field">
            <label class="general-settings__field-label" for="webhookPublicBaseUrl">Webhook public base URL</label>
            <input
              class="general-settings__text-input"
              type="url"
              id="webhookPublicBaseUrl"
              placeholder="https://hooks.example.com"
              [ngModel]="_webhookUrlValue()"
              (ngModelChange)="_webhookUrlValue.set($event)"
              aria-describedby="webhook-url-hint webhook-url-error"
              [attr.aria-invalid]="_webhookUrlError() ? true : null"
            />
            <span id="webhook-url-hint" class="general-settings__field-hint">Optional. An absolute https URL; leave empty to turn webhooks off.</span>
            <span id="webhook-url-error" class="general-settings__field-error" role="alert">{{ _webhookUrlError() ?? '' }}</span>
          </div>

          <div class="general-settings__field">
            <label class="general-settings__field-label" for="backstopIntervalMinutes">Backstop interval (minutes)</label>
            <input
              class="general-settings__number-input"
              type="number"
              id="backstopIntervalMinutes"
              [min]="BACKSTOP_INTERVAL_MIN"
              [max]="BACKSTOP_INTERVAL_MAX"
              step="1"
              [ngModel]="_backstopValue()"
              (ngModelChange)="_backstopValue.set($event)"
              aria-describedby="backstop-hint backstop-error"
              [attr.aria-invalid]="_backstopError() ? true : null"
            />
            <span id="backstop-hint" class="general-settings__field-hint">How often repositories with an active webhook are polled anyway. {{ BACKSTOP_INTERVAL_MIN }}–{{ BACKSTOP_INTERVAL_MAX }} minutes.</span>
            <span id="backstop-error" class="general-settings__field-error" role="alert">{{ _backstopError() ?? '' }}</span>
          </div>

          <div id="webhooks-error" role="alert" class="general-settings__save-error">{{ _webhooksGeneralError() ?? '' }}</div>

          <div role="status" class="general-settings__save-success">{{ settingsService.saveWebhooksSuccess() ? 'Webhook settings saved successfully' : '' }}</div>

          <button
            class="general-settings__save-btn"
            type="button"
            [disabled]="settingsService.savingWebhooks() || !isWebhooksFormValid()"
            (click)="saveWebhooks()"
          >{{ settingsService.savingWebhooks() ? 'Saving...' : 'Save' }}</button>
        </div>
      </section>
    </div>
  `,
  styleUrl: './settings-dispatch.scss',
})
export class SettingsDispatchComponent implements UnsavedChanges {
  protected readonly settingsService = inject(SettingsService);

  protected readonly MAX_CONCURRENT_MIN = MAX_CONCURRENT_MIN;
  protected readonly MAX_CONCURRENT_MAX = MAX_CONCURRENT_MAX;
  protected readonly TIMEOUT_MINUTES_MIN = TIMEOUT_MINUTES_MIN;
  protected readonly TIMEOUT_MINUTES_MAX = TIMEOUT_MINUTES_MAX;
  protected readonly PROBE_INTERVAL_MIN = PROBE_INTERVAL_MIN;
  protected readonly POLL_INTERVAL_MIN = POLL_INTERVAL_MIN;
  protected readonly POLL_INTERVAL_MAX = POLL_INTERVAL_MAX;
  protected readonly BACKSTOP_INTERVAL_MIN = BACKSTOP_INTERVAL_MIN;
  protected readonly BACKSTOP_INTERVAL_MAX = BACKSTOP_INTERVAL_MAX;

  protected readonly _maxConcurrentValue: WritableSignal<number> = signal(MAX_CONCURRENT_MIN);
  protected readonly _timeoutMinutesValue: WritableSignal<number> = signal(TIMEOUT_MINUTES_MIN);
  private _limitsInitialized = false;

  protected readonly _autoResumeValue: WritableSignal<boolean> = signal(true);
  protected readonly _probeIntervalValue: WritableSignal<number> = signal(PROBE_INTERVAL_MIN);
  protected readonly _pollIntervalValue: WritableSignal<number> = signal(POLL_INTERVAL_MIN);
  private _dispatchInitialized = false;

  protected readonly _webhookUrlValue: WritableSignal<string> = signal('');
  protected readonly _backstopValue: WritableSignal<number> = signal(BACKSTOP_INTERVAL_DEFAULT);
  private _webhooksInitialized = false;

  /** Field-keyed error for the URL: client-side rule first, then the server's keyed error code. */
  protected readonly _webhookUrlError: Signal<string | null> = computed(() => {
    if (!isValidWebhookPublicBaseUrl(this._webhookUrlValue())) {
      return 'Must be an absolute https URL without query or fragment';
    }
    return this.settingsService.saveWebhooksErrorCode() === WEBHOOK_URL_ERROR_CODE
      ? this.settingsService.saveWebhooksError()
      : null;
  });

  protected readonly _backstopError: Signal<string | null> = computed(() => {
    const value = this._backstopValue();
    if (value < BACKSTOP_INTERVAL_MIN || value > BACKSTOP_INTERVAL_MAX) {
      return `Must be between ${BACKSTOP_INTERVAL_MIN} and ${BACKSTOP_INTERVAL_MAX} minutes`;
    }
    return this.settingsService.saveWebhooksErrorCode() === BACKSTOP_ERROR_CODE
      ? this.settingsService.saveWebhooksError()
      : null;
  });

  /** A save error not keyed to either field. */
  protected readonly _webhooksGeneralError: Signal<string | null> = computed(() => {
    const code = this.settingsService.saveWebhooksErrorCode();
    return code === WEBHOOK_URL_ERROR_CODE || code === BACKSTOP_ERROR_CODE
      ? null
      : this.settingsService.saveWebhooksError();
  });

  private readonly _isWebhooksDirty: Signal<boolean> = computed(() => {
    const saved = this.settingsService.settings();
    if (saved === null) {
      return false;
    }
    return (
      this._webhookUrlValue().trim() !== (saved.webhookPublicBaseUrl ?? '') ||
      this._backstopValue() !== (saved.backstopIntervalMinutes ?? BACKSTOP_INTERVAL_DEFAULT)
    );
  });

  private readonly _isLimitsDirty: Signal<boolean> = computed(() => {
    const saved = this.settingsService.workerLimits();
    if (saved === null) {
      return false;
    }
    return (
      this._maxConcurrentValue() !== saved.maxConcurrent ||
      this._timeoutMinutesValue() !== saved.timeoutMinutes
    );
  });

  private readonly _isDispatchDirty: Signal<boolean> = computed(() => {
    const saved = this.settingsService.settings();
    if (saved === null) {
      return false;
    }
    return (
      this._autoResumeValue() !== saved.autoResumeOnUsageReset ||
      this._probeIntervalValue() !== saved.probeIntervalMinutes ||
      this._pollIntervalValue() !== saved.pollIntervalSeconds
    );
  });

  constructor() {
    effect(() => {
      const limits = this.settingsService.workerLimits();
      if (limits !== null && !this._limitsInitialized) {
        this._limitsInitialized = true;
        this._maxConcurrentValue.set(limits.maxConcurrent);
        this._timeoutMinutesValue.set(limits.timeoutMinutes);
      }
    });

    effect(() => {
      const settings = this.settingsService.settings();
      if (settings !== null && !this._dispatchInitialized) {
        this._dispatchInitialized = true;
        this._autoResumeValue.set(settings.autoResumeOnUsageReset);
        this._probeIntervalValue.set(settings.probeIntervalMinutes);
        this._pollIntervalValue.set(settings.pollIntervalSeconds);
      }
    });

    effect(() => {
      const settings = this.settingsService.settings();
      if (settings !== null && !this._webhooksInitialized) {
        this._webhooksInitialized = true;
        this._webhookUrlValue.set(settings.webhookPublicBaseUrl ?? '');
        this._backstopValue.set(settings.backstopIntervalMinutes ?? BACKSTOP_INTERVAL_DEFAULT);
      }
    });
  }

  hasUnsavedChanges(): boolean {
    return this._isLimitsDirty() || this._isDispatchDirty() || this._isWebhooksDirty();
  }

  isLimitsFormValid(): boolean {
    const maxConcurrent = this._maxConcurrentValue();
    const timeoutMinutes = this._timeoutMinutesValue();
    return (
      maxConcurrent >= MAX_CONCURRENT_MIN &&
      maxConcurrent <= MAX_CONCURRENT_MAX &&
      timeoutMinutes >= TIMEOUT_MINUTES_MIN &&
      timeoutMinutes <= TIMEOUT_MINUTES_MAX
    );
  }

  saveLimits(): void {
    this.settingsService.updateWorkerLimits(this._maxConcurrentValue(), this._timeoutMinutesValue());
  }

  isDispatchFormValid(): boolean {
    const pollInterval = this._pollIntervalValue();
    return (
      this._probeIntervalValue() >= PROBE_INTERVAL_MIN &&
      pollInterval >= POLL_INTERVAL_MIN &&
      pollInterval <= POLL_INTERVAL_MAX
    );
  }

  isWebhooksFormValid(): boolean {
    const backstop = this._backstopValue();
    return (
      isValidWebhookPublicBaseUrl(this._webhookUrlValue()) &&
      backstop >= BACKSTOP_INTERVAL_MIN &&
      backstop <= BACKSTOP_INTERVAL_MAX
    );
  }

  saveWebhooks(): void {
    const url = this._webhookUrlValue().trim();
    this.settingsService.updateWebhookSettings(url === '' ? null : url, this._backstopValue());
  }

  saveDispatch(): void {
    this.settingsService.updateDispatchSettings(this._autoResumeValue(), this._probeIntervalValue(), this._pollIntervalValue());
  }
}
