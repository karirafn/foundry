import {
  Component,
  ChangeDetectionStrategy,
  ElementRef,
  Injector,
  ViewChild,
  WritableSignal,
  afterNextRender,
  inject,
  runInInjectionContext,
  signal,
  effect,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../settings.service';
import { AuthMode } from '../settings.model';

const MAX_CONCURRENT_MIN = 1;
const MAX_CONCURRENT_MAX = 20;
const TIMEOUT_MINUTES_MIN = 1;
const TIMEOUT_MINUTES_MAX = 1440;

@Component({
  selector: 'fd-settings-general',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <div class="general-settings">
      <section class="general-settings__section">
        <h2 class="general-settings__section-title" #authHeading tabindex="-1">Worker Authentication</h2>
        <p class="general-settings__section-description">
          Configure how worker containers authenticate with the AI provider.
        </p>

        <fieldset class="general-settings__mode-fieldset">
          <legend class="sr-only">Authentication mode</legend>
          <div class="general-settings__mode-selector">
            <label class="general-settings__mode-option">
              <input
                type="radio"
                name="authMode"
                value="api_key"
                [checked]="_selectedMode() === 'api_key'"
                (change)="onModeChange('api_key')"
              />
              <span class="general-settings__mode-label">API Key</span>
            </label>
            <label class="general-settings__mode-option">
              <input
                type="radio"
                name="authMode"
                value="oauth"
                [checked]="_selectedMode() === 'oauth'"
                (change)="onModeChange('oauth')"
              />
              <span class="general-settings__mode-label">OAuth</span>
            </label>
          </div>
        </fieldset>

        @if (_selectedMode() === 'api_key') {
          <div class="general-settings__api-key-form">
            <div class="general-settings__field">
              <label class="general-settings__field-label" for="apiKey">API Key</label>
              <div class="general-settings__api-key-wrapper">
                <input
                  class="general-settings__api-key-input"
                  [type]="_showApiKey() ? 'text' : 'password'"
                  id="apiKey"
                  autocomplete="off"
                  placeholder="Enter your API key"
                  [(ngModel)]="_apiKeyValue"
                  [attr.aria-invalid]="!!settingsService.saveError() || null"
                  aria-describedby="api-key-error api-key-configured"
                />
                <button
                  class="general-settings__toggle-visibility-btn"
                  type="button"
                  [attr.aria-label]="_showApiKey() ? 'Hide API key' : 'Show API key'"
                  (click)="_showApiKey.set(!_showApiKey())"
                >
                  @if (_showApiKey()) {
                    <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                      <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94"></path>
                      <path d="M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19"></path>
                      <line x1="1" y1="1" x2="23" y2="23"></line>
                    </svg>
                  } @else {
                    <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                      <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"></path>
                      <circle cx="12" cy="12" r="3"></circle>
                    </svg>
                  }
                </button>
              </div>

              @if (settingsService.authSettings()?.apiKeyConfigured) {
                <span id="api-key-configured" class="general-settings__configured-indicator">
                  API key is configured
                </span>
              }
            </div>

            <div id="api-key-error" class="general-settings__save-error" role="alert">
              @if (settingsService.saveError()) {
                {{ settingsService.saveError() }}
              }
            </div>

            <div class="general-settings__save-success" role="status">
              @if (settingsService.saveSuccess()) {
                Settings saved successfully
              }
            </div>

            <button
              class="general-settings__save-btn"
              type="button"
              [disabled]="settingsService.saving() || !_apiKeyValue"
              (click)="saveApiKey()"
            >{{ settingsService.saving() ? 'Saving...' : 'Save' }}</button>
          </div>
        }

        @if (_selectedMode() === 'oauth') {
          <div class="general-settings__oauth-section">
            @if (settingsService.authSettings()?.oauth; as oauthInfo) {
              <div class="general-settings__oauth-grid">
                <div class="general-settings__oauth-field">
                  <span class="general-settings__oauth-field-label">Access Token</span>
                  <span class="general-settings__oauth-field-value">
                    {{ oauthInfo.accessTokenPresent ? 'Present' : 'Not present' }}
                  </span>
                </div>
                <div class="general-settings__oauth-field">
                  <span class="general-settings__oauth-field-label">Refresh Token</span>
                  <span class="general-settings__oauth-field-value">
                    {{ oauthInfo.refreshTokenPresent ? 'Present' : 'Not present' }}
                  </span>
                </div>
                @if (oauthInfo.expiresAt) {
                  <div class="general-settings__oauth-field">
                    <span class="general-settings__oauth-field-label">Expires At</span>
                    <span class="general-settings__oauth-field-value">{{ oauthInfo.expiresAt }}</span>
                  </div>
                }
                @if (oauthInfo.subscriptionType) {
                  <div class="general-settings__oauth-field">
                    <span class="general-settings__oauth-field-label">Subscription</span>
                    <span class="general-settings__oauth-field-value">{{ oauthInfo.subscriptionType }}</span>
                  </div>
                }
                <div class="general-settings__oauth-field">
                  <span class="general-settings__oauth-field-label">Status</span>
                  <span class="general-settings__oauth-field-value general-settings__oauth-status general-settings__oauth-status--{{ oauthInfo.status }}">
                    {{ oauthInfo.status }}
                  </span>
                </div>
              </div>
            } @else {
              <div class="general-settings__oauth-setup">
                <p class="general-settings__oauth-setup-instructions">
                  No OAuth credentials found. Run the following command to authenticate:
                </p>
                <code class="general-settings__oauth-setup-command">claude setup-token</code>
              </div>
            }

            <div class="general-settings__switch-error" role="alert">
              @if (settingsService.switchError()) {
                {{ settingsService.switchError() }}
              }
            </div>

            <div class="general-settings__save-success" role="status">
              @if (settingsService.saveSuccess()) {
                OAuth credentials applied successfully
              }
            </div>

            <button
              class="general-settings__scan-btn"
              type="button"
              [disabled]="settingsService.switching()"
              (click)="switchToOAuth()"
            >{{ settingsService.switching() ? 'Scanning...' : 'Scan & Apply OAuth Credentials' }}</button>
          </div>
        }
      </section>

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

          <div id="limits-error" role="alert" class="general-settings__save-error">
            @if (settingsService.saveLimitsError()) {
              {{ settingsService.saveLimitsError() }}
            }
          </div>

          <div role="status" class="general-settings__save-success">
            @if (settingsService.saveLimitsSuccess()) {
              Worker limits saved successfully
            }
          </div>

          <button
            class="general-settings__save-btn"
            type="button"
            [disabled]="settingsService.savingLimits() || !isLimitsFormValid()"
            (click)="saveLimits()"
          >{{ settingsService.savingLimits() ? 'Saving...' : 'Save' }}</button>
        </div>
      </section>
    </div>
  `,
  styleUrl: './settings-general.scss',
})
export class SettingsGeneralComponent {
  protected readonly settingsService = inject(SettingsService);
  private readonly _injector = inject(Injector);

  @ViewChild('authHeading') private readonly _authHeading?: ElementRef<HTMLHeadingElement>;

  protected readonly MAX_CONCURRENT_MIN = MAX_CONCURRENT_MIN;
  protected readonly MAX_CONCURRENT_MAX = MAX_CONCURRENT_MAX;
  protected readonly TIMEOUT_MINUTES_MIN = TIMEOUT_MINUTES_MIN;
  protected readonly TIMEOUT_MINUTES_MAX = TIMEOUT_MINUTES_MAX;

  protected readonly _selectedMode: WritableSignal<AuthMode> = signal('api_key');
  protected readonly _showApiKey: WritableSignal<boolean> = signal(false);
  protected _apiKeyValue = '';
  private _modeInitialized = false;

  protected readonly _maxConcurrentValue: WritableSignal<number> = signal(MAX_CONCURRENT_MIN);
  protected readonly _timeoutMinutesValue: WritableSignal<number> = signal(TIMEOUT_MINUTES_MIN);
  private _limitsInitialized = false;

  constructor() {
    effect(() => {
      const settings = this.settingsService.authSettings();
      if (settings !== null && !this._modeInitialized) {
        this._modeInitialized = true;
        this._selectedMode.set(settings.mode);
      }
    });

    effect(() => {
      const limits = this.settingsService.workerLimits();
      if (limits !== null && !this._limitsInitialized) {
        this._limitsInitialized = true;
        this._maxConcurrentValue.set(limits.maxConcurrent);
        this._timeoutMinutesValue.set(limits.timeoutMinutes);
      }
    });
  }

  onModeChange(mode: AuthMode): void {
    this._selectedMode.set(mode);
    runInInjectionContext(this._injector, () =>
      afterNextRender(() => this._authHeading?.nativeElement.focus())
    );
  }

  saveApiKey(): void {
    this.settingsService.updateAuthMode('api_key', this._apiKeyValue);
  }

  switchToOAuth(): void {
    this.settingsService.scanOAuthCredentials();
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
}
