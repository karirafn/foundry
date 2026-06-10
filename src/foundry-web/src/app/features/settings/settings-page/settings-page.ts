import { Component, OnInit, WritableSignal, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../settings.service';
import { AuthMode } from '../settings.model';

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

      @if (!settingsService.loading() && !settingsService.loadError()) {
        <section class="settings-page__section">
          <h2 class="settings-page__section-title">Worker Authentication</h2>
          <p class="settings-page__section-description">
            Configure how worker containers authenticate with the AI provider.
          </p>

          <div class="settings-page__mode-selector" role="radiogroup" aria-label="Authentication mode">
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
                    [attr.aria-describedby]="settingsService.authSettings()?.apiKeyConfigured ? 'api-key-configured' : null"
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
                <div class="settings-page__save-error" role="alert">{{ settingsService.saveError() }}</div>
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

              <button
                class="settings-page__scan-btn"
                type="button"
                [disabled]="settingsService.switching()"
                (click)="switchToOAuth()"
              >{{ settingsService.switching() ? 'Scanning...' : 'Scan & Apply OAuth Credentials' }}</button>
            </div>
          }
        </section>
      }
    </div>
  `,
  styleUrl: './settings-page.scss',
})
export class SettingsPageComponent implements OnInit {
  protected readonly settingsService = inject(SettingsService);

  protected readonly _selectedMode: WritableSignal<AuthMode> = signal('api_key');
  protected readonly _showApiKey: WritableSignal<boolean> = signal(false);
  private _modeInitialized = false;
  protected _apiKeyValue = '';

  constructor() {
    effect(() => {
      const settings = this.settingsService.authSettings();
      if (settings !== null && !this._modeInitialized) {
        this._modeInitialized = true;
        this._selectedMode.set(settings.mode);
      }
    });
  }

  ngOnInit(): void {
    this.settingsService.loadSettings();
  }

  onModeChange(mode: AuthMode): void {
    this._selectedMode.set(mode);
  }

  saveApiKey(): void {
    this.settingsService.updateAuthMode('api_key', this._apiKeyValue);
  }

  switchToOAuth(): void {
    this.settingsService.scanOAuthCredentials();
  }
}
