import { Component, ElementRef, OnInit, ViewChild, WritableSignal, afterNextRender, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../settings.service';
import { AuthMode } from '../settings.model';

const MAX_CONCURRENT_MIN = 1;
const MAX_CONCURRENT_MAX = 20;
const TIMEOUT_MINUTES_MIN = 1;
const TIMEOUT_MINUTES_MAX = 1440;

@Component({
  selector: 'fd-settings-page',
  standalone: true,
  imports: [RouterLink, FormsModule],
  template: `
    <div class="settings-page">
      <header class="settings-page__header">
        <a class="settings-page__back-link" routerLink="/issues">← Back to issues</a>
        <h1 class="settings-page__heading">Settings</h1>
      </header>

      @if (settingsService.loadError()) {
        <div class="settings-page__load-error" role="alert">
          <span class="settings-page__load-error-message">{{ settingsService.loadError() }}</span>
          <button
            class="settings-page__retry-btn"
            type="button"
            (click)="settingsService.loadSettings()"
          >Retry</button>
        </div>
      }

      @if (settingsService.loading()) {
        <div class="settings-page__loading" role="status" aria-label="Loading settings">
          <span class="settings-page__loading-spinner" aria-hidden="true"></span>
          <span class="sr-only">Loading settings</span>
        </div>
      }

      @if (!settingsService.loading() && !settingsService.loadError()) {
        <div class="settings-page__sections">
          <section class="settings-page__section">
            <h2 class="settings-page__section-title" #sectionHeading tabindex="-1">Worker Authentication</h2>
            <p class="settings-page__section-description">
              Configure how worker containers authenticate with the AI provider.
            </p>

            <fieldset class="settings-page__mode-fieldset">
              <legend class="sr-only">Authentication mode</legend>
              <div class="settings-page__mode-selector">
                <label class="settings-page__mode-option">
                  <input
                    type="radio"
                    name="authMode"
                    value="api_key"
                    [checked]="_selectedMode() === 'api_key'"
                    (change)="onModeChange('api_key')"
                  />
                  <span class="settings-page__mode-label">API Key</span>
                </label>
                <label class="settings-page__mode-option">
                  <input
                    type="radio"
                    name="authMode"
                    value="oauth"
                    [checked]="_selectedMode() === 'oauth'"
                    (change)="onModeChange('oauth')"
                  />
                  <span class="settings-page__mode-label">OAuth</span>
                </label>
              </div>
            </fieldset>

            @if (_selectedMode() === 'api_key') {
              <div class="settings-page__api-key-form">
                <div class="settings-page__field">
                  <label class="settings-page__field-label" for="apiKey">API Key</label>
                  <div class="settings-page__api-key-wrapper">
                    <input
                      class="settings-page__api-key-input"
                      [type]="_showApiKey() ? 'text' : 'password'"
                      id="apiKey"
                      autocomplete="off"
                      placeholder="Enter your API key"
                      [(ngModel)]="_apiKeyValue"
                      [attr.aria-invalid]="!!settingsService.saveError() || null"
                      aria-describedby="api-key-error api-key-configured"
                    />
                    <button
                      class="settings-page__toggle-visibility-btn"
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
                    <span id="api-key-configured" class="settings-page__configured-indicator">
                      API key is configured
                    </span>
                  }
                </div>

                @if (settingsService.saveError()) {
                  <div id="api-key-error" class="settings-page__save-error" role="alert">{{ settingsService.saveError() }}</div>
                }

                @if (settingsService.saveSuccess()) {
                  <div class="settings-page__save-success" role="status">Settings saved successfully</div>
                }

                <button
                  class="settings-page__save-btn"
                  type="button"
                  [disabled]="settingsService.saving() || !_apiKeyValue"
                  (click)="saveApiKey()"
                >{{ settingsService.saving() ? 'Saving...' : 'Save' }}</button>
              </div>
            }

            @if (_selectedMode() === 'oauth') {
              <div class="settings-page__oauth-section">
                @if (settingsService.authSettings()?.oauth; as oauthInfo) {
                  <div class="settings-page__oauth-grid">
                    <div class="settings-page__oauth-field">
                      <span class="settings-page__oauth-field-label">Access Token</span>
                      <span class="settings-page__oauth-field-value">
                        {{ oauthInfo.accessTokenPresent ? 'Present' : 'Not present' }}
                      </span>
                    </div>
                    <div class="settings-page__oauth-field">
                      <span class="settings-page__oauth-field-label">Refresh Token</span>
                      <span class="settings-page__oauth-field-value">
                        {{ oauthInfo.refreshTokenPresent ? 'Present' : 'Not present' }}
                      </span>
                    </div>
                    @if (oauthInfo.expiresAt) {
                      <div class="settings-page__oauth-field">
                        <span class="settings-page__oauth-field-label">Expires At</span>
                        <span class="settings-page__oauth-field-value">{{ oauthInfo.expiresAt }}</span>
                      </div>
                    }
                    @if (oauthInfo.subscriptionType) {
                      <div class="settings-page__oauth-field">
                        <span class="settings-page__oauth-field-label">Subscription</span>
                        <span class="settings-page__oauth-field-value">{{ oauthInfo.subscriptionType }}</span>
                      </div>
                    }
                    <div class="settings-page__oauth-field">
                      <span class="settings-page__oauth-field-label">Status</span>
                      <span class="settings-page__oauth-field-value settings-page__oauth-status settings-page__oauth-status--{{ oauthInfo.status }}">
                        {{ oauthInfo.status }}
                      </span>
                    </div>
                  </div>
                } @else {
                  <div class="settings-page__oauth-setup">
                    <p class="settings-page__oauth-setup-instructions">
                      No OAuth credentials found. Run the following command to authenticate:
                    </p>
                    <code class="settings-page__oauth-setup-command">claude setup-token</code>
                  </div>
                }

                @if (settingsService.switchError()) {
                  <div class="settings-page__switch-error" role="alert">{{ settingsService.switchError() }}</div>
                }

                @if (settingsService.saveSuccess()) {
                  <div class="settings-page__save-success" role="status">OAuth credentials applied successfully</div>
                }

                <button
                  class="settings-page__scan-btn"
                  type="button"
                  [disabled]="settingsService.switching()"
                  (click)="switchToOAuth()"
                >{{ settingsService.switching() ? 'Scanning...' : 'Scan & Apply OAuth Credentials' }}</button>
              </div>
            }
          </section>

          <section class="settings-page__section">
            <h2 class="settings-page__section-title">Worker Limits</h2>
            <p class="settings-page__section-description">
              Control the maximum number of concurrent workers and the timeout for each worker run.
            </p>

            <div class="settings-page__limits-form">
              <div class="settings-page__limits-fields">
                <div class="settings-page__field">
                  <label class="settings-page__field-label" for="maxConcurrent">Max Concurrent Workers</label>
                  <input
                    class="settings-page__number-input"
                    type="number"
                    id="maxConcurrent"
                    [min]="maxConcurrentMin"
                    [max]="maxConcurrentMax"
                    step="1"
                    [ngModel]="_maxConcurrentValue()"
                    (ngModelChange)="_maxConcurrentValue.set($event)"
                    aria-describedby="max-concurrent-hint limits-error"
                  />
                  <span id="max-concurrent-hint" class="settings-page__field-hint">1–20 workers</span>
                </div>
                <div class="settings-page__field">
                  <label class="settings-page__field-label" for="timeoutMinutes">Timeout (minutes)</label>
                  <input
                    class="settings-page__number-input"
                    type="number"
                    id="timeoutMinutes"
                    [min]="timeoutMinutesMin"
                    [max]="timeoutMinutesMax"
                    step="1"
                    [ngModel]="_timeoutMinutesValue()"
                    (ngModelChange)="_timeoutMinutesValue.set($event)"
                    aria-describedby="timeout-minutes-hint limits-error"
                  />
                  <span id="timeout-minutes-hint" class="settings-page__field-hint">1–1,440 minutes</span>
                </div>
              </div>

              <div id="limits-error" role="alert" class="settings-page__save-error">
                @if (settingsService.saveLimitsError()) {
                  {{ settingsService.saveLimitsError() }}
                }
              </div>

              <div role="status" class="settings-page__save-success">
                @if (settingsService.saveLimitsSuccess()) {
                  Worker limits saved successfully
                }
              </div>

              <button
                class="settings-page__save-btn"
                type="button"
                [disabled]="settingsService.savingLimits() || !isLimitsFormValid()"
                (click)="saveLimits()"
              >{{ settingsService.savingLimits() ? 'Saving...' : 'Save' }}</button>
            </div>
          </section>
        </div>
      }
    </div>
  `,
  styleUrl: './settings-page.scss',
})
export class SettingsPageComponent implements OnInit {
  protected readonly settingsService = inject(SettingsService);

  @ViewChild('sectionHeading') private readonly _sectionHeading?: ElementRef<HTMLElement>;

  protected readonly maxConcurrentMin = MAX_CONCURRENT_MIN;
  protected readonly maxConcurrentMax = MAX_CONCURRENT_MAX;
  protected readonly timeoutMinutesMin = TIMEOUT_MINUTES_MIN;
  protected readonly timeoutMinutesMax = TIMEOUT_MINUTES_MAX;

  protected readonly _selectedMode: WritableSignal<AuthMode> = signal('api_key');
  protected readonly _showApiKey: WritableSignal<boolean> = signal(false);
  private _modeInitialized = false;
  protected _apiKeyValue = '';

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

  ngOnInit(): void {
    this.settingsService.loadSettings();
  }

  onModeChange(mode: AuthMode): void {
    this._selectedMode.set(mode);
    afterNextRender(() => {
      this._sectionHeading?.nativeElement.focus();
    });
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
