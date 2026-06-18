import { Component, ChangeDetectionStrategy, WritableSignal, computed, effect, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../../settings/settings.service';
import { AuthMode } from '../../settings/settings.model';

@Component({
  selector: 'fd-setup-auth-step',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <div class="setup-auth-step">
      <h2 class="setup-auth-step__title">Worker Authentication</h2>
      <p class="setup-auth-step__description">
        Configure how worker containers authenticate with Claude.
      </p>

      <div class="setup-auth-step__form">
        <fieldset class="setup-auth-step__mode-fieldset">
          <legend class="sr-only">Authentication mode</legend>
          <div class="setup-auth-step__mode-selector">
            <label class="setup-auth-step__mode-option">
              <input
                type="radio"
                name="authMode"
                value="api_key"
                [checked]="_selectedMode() === 'api_key'"
                (change)="onModeChange('api_key')"
              />
              <span class="setup-auth-step__mode-label">API Key</span>
            </label>
            <label class="setup-auth-step__mode-option">
              <input
                type="radio"
                name="authMode"
                value="oauth"
                [checked]="_selectedMode() === 'oauth'"
                (change)="onModeChange('oauth')"
              />
              <span class="setup-auth-step__mode-label">OAuth</span>
            </label>
          </div>
        </fieldset>

        @if (_selectedMode() === 'api_key') {
          <div class="setup-auth-step__field">
            <label class="setup-auth-step__field-label" for="apiKey">API Key</label>
            <input
              class="setup-auth-step__api-key-input"
              type="password"
              id="apiKey"
              autocomplete="off"
              placeholder="Enter your API key"
              required
              [ngModel]="_apiKeyValue()"
              (ngModelChange)="_apiKeyValue.set($event)"
              [attr.aria-invalid]="!!_settingsService.saveError() || null"
              aria-describedby="api-key-error"
            />
          </div>

          <div id="api-key-error" class="setup-auth-step__error" role="alert">{{ _settingsService.saveError() ?? '' }}</div>
        }

        @if (_selectedMode() === 'oauth') {
          <div class="setup-auth-step__oauth-section">
            @if (_settingsService.authSettings()?.oauth; as oauthInfo) {
              <div class="setup-auth-step__oauth-grid">
                <div class="setup-auth-step__oauth-field">
                  <span class="setup-auth-step__oauth-field-label">Access Token</span>
                  <span class="setup-auth-step__oauth-field-value">
                    {{ oauthInfo.accessTokenPresent ? 'Present' : 'Not present' }}
                  </span>
                </div>
                <div class="setup-auth-step__oauth-field">
                  <span class="setup-auth-step__oauth-field-label">Refresh Token</span>
                  <span class="setup-auth-step__oauth-field-value">
                    {{ oauthInfo.refreshTokenPresent ? 'Present' : 'Not present' }}
                  </span>
                </div>
                @if (oauthInfo.expiresAt) {
                  <div class="setup-auth-step__oauth-field">
                    <span class="setup-auth-step__oauth-field-label">Expires At</span>
                    <span class="setup-auth-step__oauth-field-value">{{ oauthInfo.expiresAt }}</span>
                  </div>
                }
                @if (oauthInfo.subscriptionType) {
                  <div class="setup-auth-step__oauth-field">
                    <span class="setup-auth-step__oauth-field-label">Subscription</span>
                    <span class="setup-auth-step__oauth-field-value">{{ oauthInfo.subscriptionType }}</span>
                  </div>
                }
                <div class="setup-auth-step__oauth-field">
                  <span class="setup-auth-step__oauth-field-label">Status</span>
                  <span class="setup-auth-step__oauth-field-value setup-auth-step__oauth-status setup-auth-step__oauth-status--{{ oauthInfo.status }}">
                    {{ oauthInfo.status }}
                  </span>
                </div>
              </div>
            } @else {
              <div class="setup-auth-step__oauth-setup">
                <p class="setup-auth-step__oauth-setup-instructions">
                  Run the following command to authenticate, then scan for credentials:
                </p>
                <code class="setup-auth-step__oauth-setup-command">claude setup-token</code>
              </div>
            }

            <div class="setup-auth-step__error" role="alert">{{ _settingsService.switchError() ?? '' }}</div>

            <button
              class="setup-auth-step__scan-btn"
              type="button"
              [disabled]="_settingsService.switching()"
              (click)="onScanOAuth()"
            >{{ _settingsService.switching() ? 'Scanning...' : 'Scan & Apply OAuth Credentials' }}</button>
          </div>
        }

        <button
          class="setup-auth-step__next-btn"
          type="button"
          [disabled]="_isNextDisabled()"
          (click)="onNext()"
        >Next</button>
      </div>
    </div>
  `,
  styleUrl: './setup-auth-step.scss',
})
export class SetupAuthStepComponent {
  protected readonly _settingsService = inject(SettingsService);

  readonly complete = output<void>();

  protected readonly _selectedMode: WritableSignal<AuthMode | null> = signal(null);
  protected readonly _apiKeyValue: WritableSignal<string> = signal('');

  private readonly _hasSaved: WritableSignal<boolean> = signal(false);

  protected readonly _isNextDisabled = computed(() => {
    const mode = this._selectedMode();
    if (mode === null) {
      return true;
    }
    if (mode === 'api_key') {
      return !this._apiKeyValue() || this._settingsService.saving();
    }
    const oauthStatus = this._settingsService.authSettings()?.oauth?.status;
    return oauthStatus !== 'valid' || this._settingsService.switching();
  });

  constructor() {
    effect(() => {
      const hasSaved = this._hasSaved();
      const saving = this._settingsService.saving();
      const saveSuccess = this._settingsService.saveSuccess();

      if (hasSaved && !saving && saveSuccess) {
        this.complete.emit();
      }
    });
  }

  onModeChange(mode: AuthMode): void {
    this._selectedMode.set(mode);
  }

  onScanOAuth(): void {
    this._settingsService.scanOAuthCredentials();
  }

  onNext(): void {
    const mode = this._selectedMode();
    if (mode === 'api_key') {
      this._hasSaved.set(true);
      this._settingsService.updateAuthMode('api_key', this._apiKeyValue());
    } else if (mode === 'oauth') {
      this.complete.emit();
    }
  }
}
