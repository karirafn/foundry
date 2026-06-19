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
          <label class="setup-account-step__field-label" for="account-name">Account Name</label>
          <input
            class="setup-account-step__input"
            type="text"
            id="account-name"
            autocomplete="off"
            [value]="_name()"
            (input)="_name.set($any($event.target).value)"
            required
          />
        </div>

        <div class="setup-account-step__field">
          <span class="setup-account-step__field-label">Provider</span>
          <fd-provider-selector
            [provider]="_provider()"
            (providerChange)="_provider.set($event)"
            (defaultBaseUrlChange)="onDefaultBaseUrlChange($event)"
          />
        </div>

        <div class="setup-account-step__field">
          <label class="setup-account-step__field-label" for="account-base-url">Base URL</label>
          <input
            class="setup-account-step__input"
            type="text"
            id="account-base-url"
            autocomplete="off"
            [value]="_baseUrl()"
            (input)="onBaseUrlInput($any($event.target).value)"
          />
        </div>

        <div class="setup-account-step__field">
          <label class="setup-account-step__field-label" for="account-token">Token</label>
          <div class="setup-account-step__token-wrapper">
            <input
              class="setup-account-step__input"
              [type]="_showToken() ? 'text' : 'password'"
              id="account-token"
              autocomplete="off"
              [value]="_token()"
              (input)="_token.set($any($event.target).value)"
              required
              aria-describedby="account-token-validation account-save-error"
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
            <button
              class="setup-account-step__validate-btn"
              type="button"
              [disabled]="!_canValidate()"
              [attr.aria-busy]="_accountService.validating() || null"
              (click)="onValidate()"
            >{{ _accountService.validating() ? 'Validating...' : 'Validate Token' }}</button>
          </div>
        </div>

        <div
          id="account-token-validation"
          class="setup-account-step__validation-result"
          role="status"
          aria-live="polite"
        >
          <span
            class="setup-account-step__validation-dot"
            [class]="'setup-account-step__validation-dot' + (_validationModifier() ? ' setup-account-step__validation-dot--' + _validationModifier() : '')"
            [style.display]="_validationModifier() ? '' : 'none'"
            aria-hidden="true"
          ></span>
          <span
            class="setup-account-step__validation-message"
            [class]="'setup-account-step__validation-message' + (_validationModifier() ? ' setup-account-step__validation-message--' + _validationModifier() : '')"
          >{{ _validationMessage() }}</span>
        </div>

        <div
          id="account-save-error"
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

  protected readonly _name: WritableSignal<string> = signal('');
  protected readonly _provider: WritableSignal<ProviderType> = signal('GitHub');
  protected readonly _baseUrl: WritableSignal<string> = signal(GITHUB_BASE_URL);
  protected readonly _baseUrlManuallyEdited: WritableSignal<boolean> = signal(false);
  protected readonly _token: WritableSignal<string> = signal('');
  protected readonly _showToken: WritableSignal<boolean> = signal(false);

  private readonly _hasSaved: WritableSignal<boolean> = signal(false);

  protected readonly _canCreate: Signal<boolean> = computed(() => {
    if (this._accountService.saving()) {
      return false;
    }
    return !!this._name() && !!this._token();
  });

  protected readonly _canValidate: Signal<boolean> = computed(() => {
    if (this._accountService.validating()) {
      return false;
    }
    return !!this._token() && !!this._baseUrl();
  });

  protected readonly _validationModifier: Signal<'valid' | 'error' | 'warning' | null> = computed(() => {
    const result = this._accountService.validationResult();
    if (!result) {
      return null;
    }
    if (result.isValid) {
      return 'valid';
    }
    if (result.isAuthFailure) {
      return 'error';
    }
    return 'warning';
  });

  protected readonly _validationMessage: Signal<string> = computed(() => {
    const result = this._accountService.validationResult();
    if (!result) {
      return '';
    }
    if (result.isValid) {
      return 'Token is valid';
    }
    if (result.isAuthFailure) {
      return 'Authentication failed — check that the token is correct';
    }
    return `Missing required scopes: ${result.missingScopes.join(', ')}`;
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
  }

  onDefaultBaseUrlChange(defaultUrl: string): void {
    if (!this._baseUrlManuallyEdited()) {
      this._baseUrl.set(defaultUrl);
    }
  }

  onCreate(): void {
    this._hasSaved.set(true);
    this._accountService.createAccount({
      name: this._name(),
      providerType: this._provider(),
      baseUrl: this._baseUrl(),
      token: this._token(),
    });
  }

  onValidate(): void {
    this._accountService.validateToken({
      token: this._token(),
      baseUrl: this._baseUrl(),
    });
  }
}
