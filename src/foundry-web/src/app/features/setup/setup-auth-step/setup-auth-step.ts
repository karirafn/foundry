import { Component, ChangeDetectionStrategy, WritableSignal, computed, effect, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../../settings/settings.service';
import { AuthMode } from '../../settings/settings.model';
import { OAuthPanelComponent } from '../../settings/oauth-panel/oauth-panel';

@Component({
  selector: 'fd-setup-auth-step',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, OAuthPanelComponent],
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
            <fd-oauth-panel
              [status]="_oauthStatus()"
              [expiresAt]="_settingsService.authSettings()?.oauth?.expiresAt ?? null"
              [subscriptionType]="_settingsService.authSettings()?.oauth?.subscriptionType ?? null"
              (refresh)="onOAuthRefresh()"
            />

            @if (_oauthStatus() !== 'Present') {
              <div role="note" class="setup-auth-step__oauth-note">
                You haven't logged in yet. You can finish setup now, but workers won't run until you complete the login command above.
              </div>
            }
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

  protected readonly _oauthStatus = computed(
    () => this._settingsService.authSettings()?.oauth?.status ?? 'NotConfigured' as const
  );

  protected readonly _isNextDisabled = computed(() => {
    const mode = this._selectedMode();
    if (mode === null) {
      return true;
    }
    if (mode === 'api_key') {
      return !this._apiKeyValue() || this._settingsService.saving();
    }
    // OAuth: Next is enabled once OAuth mode is selected/configured
    return false;
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

  onOAuthRefresh(): void {
    this._settingsService.loadSettings();
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
