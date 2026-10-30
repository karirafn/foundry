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

  protected readonly _maxConcurrentValue: WritableSignal<number> = signal(MAX_CONCURRENT_MIN);
  protected readonly _timeoutMinutesValue: WritableSignal<number> = signal(TIMEOUT_MINUTES_MIN);
  private _limitsInitialized = false;

  protected readonly _autoResumeValue: WritableSignal<boolean> = signal(true);
  protected readonly _probeIntervalValue: WritableSignal<number> = signal(PROBE_INTERVAL_MIN);
  protected readonly _pollIntervalValue: WritableSignal<number> = signal(POLL_INTERVAL_MIN);
  private _dispatchInitialized = false;

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
  }

  hasUnsavedChanges(): boolean {
    return this._isLimitsDirty() || this._isDispatchDirty();
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

  saveDispatch(): void {
    this.settingsService.updateDispatchSettings(this._autoResumeValue(), this._probeIntervalValue(), this._pollIntervalValue());
  }
}
