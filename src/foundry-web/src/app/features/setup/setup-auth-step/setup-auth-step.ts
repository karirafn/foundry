import { Component, ChangeDetectionStrategy, WritableSignal, effect, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../../settings/settings.service';

@Component({
  selector: 'fd-setup-auth-step',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <div class="setup-auth-step">
      <h2 class="setup-auth-step__title">Worker Authentication</h2>
      <p class="setup-auth-step__description">
        Enter your Anthropic API key. Workers will use this key to authenticate with Claude.
      </p>

      <div class="setup-auth-step__form">
        <div class="setup-auth-step__field">
          <label class="setup-auth-step__field-label" for="apiKey">API Key</label>
          <input
            class="setup-auth-step__api-key-input"
            type="password"
            id="apiKey"
            autocomplete="off"
            placeholder="Enter your API key"
            required
            [(ngModel)]="_apiKeyValue"
            [attr.aria-invalid]="!!_settingsService.saveError() || null"
            aria-describedby="api-key-error"
          />
        </div>

        <div id="api-key-error" class="setup-auth-step__error" role="alert">{{ _settingsService.saveError() ?? '' }}</div>

        <button
          class="setup-auth-step__next-btn"
          type="button"
          [disabled]="_settingsService.saving() || !_apiKeyValue"
          (click)="onNext()"
        >{{ _settingsService.saving() ? 'Saving...' : 'Next' }}</button>
      </div>
    </div>
  `,
  styleUrl: './setup-auth-step.scss',
})
export class SetupAuthStepComponent {
  protected readonly _settingsService = inject(SettingsService);

  readonly complete = output<void>();

  protected _apiKeyValue = '';

  private readonly _hasSaved: WritableSignal<boolean> = signal(false);

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

  onNext(): void {
    this._hasSaved.set(true);
    this._settingsService.updateAuthMode('api_key', this._apiKeyValue);
  }
}
