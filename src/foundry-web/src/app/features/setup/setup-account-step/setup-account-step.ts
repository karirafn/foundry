import {
  ChangeDetectionStrategy,
  Component,
  OutputEmitterRef,
  Signal,
  WritableSignal,
  computed,
  effect,
  inject,
  output,
  signal,
} from '@angular/core';
import { AccountService } from '../../settings/accounts/account.service';
import { ProviderType } from '../../settings/accounts/account.model';
import { ProviderSelectorComponent } from '../../settings/accounts/provider-selector/provider-selector';

const GITHUB_BASE_URL = 'https://github.com';

@Component({
  selector: 'fd-setup-account-step',
  standalone: true,
  imports: [ProviderSelectorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="setup-account-step">
      <h2 class="setup-account-step__title">Add a Provider Account</h2>
      <p class="setup-account-step__description">
        Connect an account to get started.
      </p>

      <div class="setup-account-step__form">
        <div class="setup-account-step__field">
          <span id="setup-provider-label" class="setup-account-step__field-label">Provider</span>
          <fd-provider-selector
            [provider]="_provider()"
            (providerChange)="_provider.set($event)"
            (defaultBaseUrlChange)="onDefaultBaseUrlChange($event)"
            [ariaLabelledBy]="'setup-provider-label'"
          />
        </div>

        <div class="setup-account-step__field">
          <label class="setup-account-step__field-label" for="setup-base-url">Base URL</label>
          <input
            class="setup-account-step__input"
            type="text"
            id="setup-base-url"
            autocomplete="off"
            [value]="_baseUrl()"
            (input)="onBaseUrlInput($any($event.target).value)"
            (blur)="onBaseUrlBlur()"
          />
        </div>

        <div class="setup-account-step__field">
          <label class="setup-account-step__field-label" for="setup-token">Token</label>
          <div
            class="setup-account-step__token-wrapper"
          >
            <input
              class="setup-account-step__input"
              [type]="_showToken() ? 'text' : 'password'"
              id="setup-token"
              autocomplete="off"
              [value]="_token()"
              (input)="onTokenInput($any($event.target).value)"
              (blur)="onTokenBlur()"
              (paste)="onTokenPaste()"
              required
              aria-describedby="setup-token-validation setup-save-error"
            />
            <button
              class="setup-account-step__toggle-visibility-btn"
              type="button"
              [attr.aria-label]="_showToken() ? 'Hide token' : 'Show token'"
              (click)="_showToken.set(!_showToken())"
            >
              @if (_showToken()) {
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
        </div>

        <div
          id="setup-token-validation"
          class="setup-account-step__validation-result"
          role="status"
          aria-live="polite"
        >
          @if (_accountService.validating()) {
            <span class="setup-account-step__validation-message">Resolving identity…</span>
          } @else if (_resultVisible() && _accountService.validationResult(); as result) {
            @if (result.isValid && result.accountName) {
              <span class="setup-account-step__validation-dot setup-account-step__validation-dot--valid" aria-hidden="true"></span>
              <span class="setup-account-step__validation-message setup-account-step__validation-message--valid">
                Authenticated as <span class="setup-account-step__account-name">{{ result.accountName }}</span>
              </span>
            } @else if (result.isAuthFailure) {
              <span class="setup-account-step__validation-dot setup-account-step__validation-dot--error" aria-hidden="true"></span>
              <span class="setup-account-step__validation-message setup-account-step__validation-message--error">
                Authentication failed — check that the token is correct
              </span>
            } @else if (!result.isValid && result.missingScopes.length > 0) {
              <span class="setup-account-step__validation-dot setup-account-step__validation-dot--warning" aria-hidden="true"></span>
              <span class="setup-account-step__validation-message setup-account-step__validation-message--warning">
                Missing required scopes: {{ result.missingScopes.join(', ') }}
              </span>
            } @else if (result.isValid && !result.accountName) {
              <span class="setup-account-step__validation-dot setup-account-step__validation-dot--error" aria-hidden="true"></span>
              <span class="setup-account-step__validation-message setup-account-step__validation-message--error">
                Token is valid, but the account identity could not be resolved from the provider
              </span>
            }
          }
        </div>

        <div
          id="setup-save-error"
          class="setup-account-step__save-error"
          role="alert"
        >{{ _accountService.saveError() ?? '' }}</div>

        <div class="setup-account-step__actions">
          <button
            class="setup-account-step__back-btn"
            type="button"
            (click)="back.emit()"
          >Back</button>

          <button
            class="setup-account-step__create-btn"
            type="button"
            [disabled]="!_canCreate()"
            (click)="onCreate()"
          >{{ _accountService.saving() ? 'Creating...' : 'Create Account' }}</button>
        </div>
      </div>
    </div>
  `,
  styleUrl: './setup-account-step.scss',
})
export class SetupAccountStepComponent {
  protected readonly _accountService = inject(AccountService);

  readonly complete: OutputEmitterRef<string> = output<string>();
  readonly back: OutputEmitterRef<void> = output<void>();

  protected readonly _provider: WritableSignal<ProviderType> = signal('GitHub');
  protected readonly _baseUrl: WritableSignal<string> = signal(GITHUB_BASE_URL);
  protected readonly _baseUrlManuallyEdited: WritableSignal<boolean> = signal(false);
  protected readonly _token: WritableSignal<string> = signal('');
  protected readonly _showToken: WritableSignal<boolean> = signal(false);

  /** Tracks the last (token, baseUrl) pair sent to resolution to avoid duplicate calls. */
  private readonly _lastResolvedPair: WritableSignal<{ token: string; baseUrl: string } | null> = signal(null);

  /** Whether a held resolution is pending (token present but baseUrl was empty at blur time). */
  private readonly _pendingResolution: WritableSignal<boolean> = signal(false);

  private readonly _hasSaved: WritableSignal<boolean> = signal(false);

  /** True only when the last resolved pair still matches current inputs — hides stale results after edits. */
  protected readonly _resultVisible: Signal<boolean> = computed(() => {
    const last = this._lastResolvedPair();
    if (!last) {
      return false;
    }
    return last.token === this._token() && last.baseUrl === this._baseUrl();
  });

  protected readonly _canCreate: Signal<boolean> = computed(() => {
    if (this._accountService.saving()) {
      return false;
    }
    if (!this._token()) {
      return false;
    }
    if (!this._resultVisible()) {
      return false;
    }
    const result = this._accountService.validationResult();
    return result !== null && result.isValid && !!result.accountName;
  });

  constructor() {
    effect(() => {
      const hasSaved = this._hasSaved();
      const saving = this._accountService.saving();
      const saveSuccess = this._accountService.saveSuccess();
      const accounts = this._accountService.accounts();

      if (hasSaved && !saving && saveSuccess) {
        const created = accounts[accounts.length - 1];
        if (created) {
          this.complete.emit(created.id);
        }
      }
    });
  }

  onBaseUrlInput(value: string): void {
    this._baseUrl.set(value);
    this._baseUrlManuallyEdited.set(true);
    this._clearResolution();
  }

  onBaseUrlBlur(): void {
    if (this._pendingResolution() && this._token() && this._baseUrl()) {
      this._pendingResolution.set(false);
      this._triggerResolution();
    }
  }

  onDefaultBaseUrlChange(defaultUrl: string): void {
    if (!this._baseUrlManuallyEdited()) {
      this._baseUrl.set(defaultUrl);
    }
  }

  onTokenInput(value: string): void {
    this._token.set(value);
    this._clearResolution();
  }

  onTokenBlur(): void {
    const token = this._token();
    const baseUrl = this._baseUrl();
    if (!token) {
      return;
    }
    if (!baseUrl) {
      this._pendingResolution.set(true);
      return;
    }
    this._triggerResolution();
  }

  onTokenPaste(): void {
    setTimeout(() => {
      const token = this._token();
      const baseUrl = this._baseUrl();
      if (!token || !baseUrl) {
        return;
      }
      this._triggerResolution();
    }, 0);
  }

  onCreate(): void {
    this._hasSaved.set(true);
    this._accountService.createAccount({
      providerType: this._provider(),
      baseUrl: this._baseUrl(),
      token: this._token(),
    });
  }

  private _clearResolution(): void {
    this._lastResolvedPair.set(null);
  }

  private _triggerResolution(): void {
    const token = this._token();
    const baseUrl = this._baseUrl();
    const last = this._lastResolvedPair();
    if (last && last.token === token && last.baseUrl === baseUrl) {
      return;
    }
    this._lastResolvedPair.set({ token, baseUrl });
    this._accountService.validateToken({ token, baseUrl });
  }
}
