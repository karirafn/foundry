import {
  Component,
  ChangeDetectionStrategy,
  ElementRef,
  Injector,
  Signal,
  ViewChild,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  output,
  runInInjectionContext,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../../../core/services/settings.service';
import { ApiKeyStatus, AuthMode } from '../../../core/models/settings.model';
import { OAuthPanelComponent } from '../../settings/oauth-panel/oauth-panel';

@Component({
  selector: 'fd-setup-auth-step',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, OAuthPanelComponent],
  template: `
    <div class="setup-auth-step">
      <h2 class="setup-auth-step__title" #authHeading tabindex="-1">Worker Authentication</h2>
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
              aria-describedby="api-key-error api-key-unreadable-note"
            />
          </div>

          <div id="api-key-error" class="setup-auth-step__error" role="alert">{{ _settingsService.saveError() ?? '' }}</div>

          <!-- Persistent live-region wrapper: always in the DOM so the announcement fires
               on page load when the operator arrives in the Unreadable state.
               role="status" (polite) avoids a double assertive announcement alongside
               the existing #api-key-error role="alert". The --hidden modifier suppresses
               the visible box when no Unreadable content is active.
               id="api-key-unreadable-note" is referenced by aria-describedby on the API key
               input so screen-reader users hear the recovery guidance when focusing the field. -->
          <div
            id="api-key-unreadable-note"
            class="setup-auth-step__oauth-note setup-auth-step__note--warning"
            [class.setup-auth-step__note--warning-hidden]="_apiKeyStatus() !== 'Unreadable'"
            role="status"
          >
            @if (_apiKeyStatus() === 'Unreadable') {
              <svg class="setup-auth-step__note-warning-icon" xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
                <line x1="12" y1="9" x2="12" y2="13"></line>
                <line x1="12" y1="17" x2="12.01" y2="17"></line>
              </svg>
              <span>
                The saved API key can't be read (its encryption key was lost). Enter a new
                API key above, or choose OAuth and sign in.
              </span>
            }
          </div>

          @if (_apiKeyStatus() === 'NotConfigured') {
            <div class="setup-auth-step__oauth-note">
              No API key is set yet. Enter one above to let workers run.
            </div>
          }
        }

        @if (_selectedMode() === 'oauth') {
          <div class="setup-auth-step__oauth-section">
            <fd-oauth-panel
              [status]="_oauthStatus()"
              [subscriptionType]="_settingsService.authSettings()?.oauth?.subscriptionType ?? null"
              [accountEmail]="_settingsService.authSettings()?.accountEmail ?? null"
              [accountOrgName]="_settingsService.authSettings()?.accountOrgName ?? null"
              [loginPhase]="_settingsService.loginPhase()"
              [loginUrl]="_settingsService.loginUrl()"
              [loginError]="_settingsService.loginError()"
              (startLogin)="_settingsService.startLogin()"
              (submitCode)="_settingsService.submitLoginCode($event)"
              (cancel)="cancelLogin()"
            />

            <div role="alert" class="setup-auth-step__error">{{ (!_settingsService.loginPhase() && _settingsService.startLoginError()) ? _settingsService.startLoginError() : '' }}</div>

            @if (_oauthStatus() !== 'Present' && !_settingsService.loginPhase()) {
              <div class="setup-auth-step__oauth-note">
                You haven't logged in yet. You can finish setup now, but workers won't run until you sign in.
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
  private readonly _injector = inject(Injector);
  private readonly _elementRef = inject(ElementRef);

  @ViewChild('authHeading') private readonly _authHeading?: ElementRef<HTMLHeadingElement>;

  readonly complete = output<void>();

  protected readonly _selectedMode: WritableSignal<AuthMode | null> = signal(null);
  protected readonly _apiKeyValue: WritableSignal<string> = signal('');

  private readonly _hasSaved: WritableSignal<boolean> = signal(false);

  protected readonly _oauthStatus = computed(
    () => this._settingsService.authSettings()?.oauth?.status ?? 'NotConfigured' as const
  );

  protected readonly _apiKeyStatus: Signal<ApiKeyStatus> = computed(
    () => this._settingsService.authSettings()?.apiKeyStatus ?? 'NotConfigured'
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

    // Move focus to auth heading on Succeeded (Finding 4).
    effect(() => {
      const phase = this._settingsService.loginPhase();
      if (phase === 'Succeeded') {
        runInInjectionContext(this._injector, () =>
          afterNextRender(() => this._authHeading?.nativeElement.focus())
        );
      }
    });
  }

  onModeChange(mode: AuthMode): void {
    this._selectedMode.set(mode);
  }

  cancelLogin(): void {
    this._settingsService.cancelLogin();
    // Return focus to the entry "Log in" button; fallback to the step heading.
    runInInjectionContext(this._injector, () =>
      afterNextRender(() => {
        const host = this._elementRef.nativeElement as HTMLElement;
        const entryBtn = host.querySelector<HTMLButtonElement>('.oauth-panel__login-btn');
        if (entryBtn) {
          entryBtn.focus();
        } else {
          this._authHeading?.nativeElement.focus();
        }
      })
    );
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
