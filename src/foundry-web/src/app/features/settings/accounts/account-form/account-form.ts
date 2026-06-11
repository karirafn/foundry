import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  InputSignal,
  OnInit,
  OutputEmitterRef,
  Signal,
  ViewChild,
  WritableSignal,
  computed,
  input,
  output,
  signal,
} from '@angular/core';
import {
  AccountSummary,
  CreateAccountRequest,
  TokenValidationResult,
  UpdateAccountRequest,
} from '../account.model';

const DEFAULT_BASE_URL = 'https://github.com';

@Component({
  selector: 'fd-account-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="account-form">
      <button
        class="account-form__cancel-link"
        type="button"
        (click)="cancel.emit()"
      >
        <span aria-hidden="true">←</span> Cancel
      </button>

      <h2 class="account-form__heading" #formHeading tabindex="-1">
        {{ _isEditMode() ? 'Edit Account' : 'Add Account' }}
      </h2>

      <div class="account-form__field">
        <label class="account-form__field-label" for="account-name">Account Name</label>
        <input
          class="account-form__input"
          type="text"
          id="account-name"
          [value]="_name()"
          (input)="_name.set($any($event.target).value)"
          autocomplete="off"
        />
      </div>

      @if (_isEditMode()) {
        <div class="account-form__field">
          <span class="account-form__field-label">Provider</span>
          <span class="account-form__provider-badge">{{ account()!.providerType }}</span>
        </div>
      } @else {
        <fieldset class="account-form__provider-selector">
          <legend class="sr-only">Provider type</legend>
          <label class="account-form__provider-option">
            <input type="radio" name="providerType" value="GitHub" checked />
            GitHub
          </label>
          <label class="account-form__provider-option account-form__provider-option--disabled" aria-disabled="true">
            <input type="radio" name="providerType" value="GitLab" disabled />
            GitLab (coming soon)
          </label>
        </fieldset>
      }

      <div class="account-form__field">
        <label class="account-form__field-label" for="account-base-url">Base URL</label>
        <input
          class="account-form__input"
          type="text"
          id="account-base-url"
          [value]="_baseUrl()"
          (input)="_baseUrl.set($any($event.target).value)"
          autocomplete="off"
        />
      </div>

      <div class="account-form__field">
        <label class="account-form__field-label" for="account-token">Token</label>
        <div class="account-form__token-wrapper">
          <input
            class="account-form__input"
            [type]="_showToken() ? 'text' : 'password'"
            id="account-token"
            [value]="_token()"
            (input)="_token.set($any($event.target).value)"
            autocomplete="off"
            aria-describedby="account-token-hint account-token-validation account-token-error"
          />
          <button
            class="account-form__toggle-visibility-btn"
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
            class="account-form__validate-btn"
            type="button"
            [disabled]="!_canValidate()"
            (click)="validateToken.emit({ token: _token(), baseUrl: _baseUrl() })"
          >{{ validating() ? 'Validating...' : 'Validate Token' }}</button>
        </div>

        @if (_isEditMode()) {
          <span id="account-token-hint" class="account-form__field-hint">
            Leave empty to keep current token
          </span>
        }
      </div>

      @if (validationResult()) {
        <div
          id="account-token-validation"
          class="account-form__validation-result"
          role="status"
          aria-live="polite"
        >
          @if (validationResult()!.isValid) {
            <span class="account-form__validation-dot account-form__validation-dot--valid" aria-hidden="true"></span>
            <span class="account-form__validation-message account-form__validation-message--valid">Token is valid</span>
          } @else if (validationResult()!.isAuthFailure) {
            <span class="account-form__validation-dot account-form__validation-dot--error" aria-hidden="true"></span>
            <span class="account-form__validation-message account-form__validation-message--error">Authentication failed — check that the token is correct</span>
          } @else {
            <span class="account-form__validation-dot account-form__validation-dot--warning" aria-hidden="true"></span>
            <span class="account-form__validation-message account-form__validation-message--warning">Missing required scopes: {{ validationResult()!.missingScopes.join(', ') }}</span>
          }
        </div>
      }

      @if (saveError()) {
        <div
          id="account-token-error"
          class="account-form__save-error"
          role="alert"
        >{{ saveError() }}</div>
      }

      <button
        class="account-form__save-btn"
        type="button"
        [disabled]="!_canSave()"
        (click)="onSave()"
      >Save</button>
    </div>
  `,
  styleUrl: './account-form.scss',
})
export class AccountFormComponent implements OnInit {
  readonly account: InputSignal<AccountSummary | null> = input<AccountSummary | null>(null);
  readonly saving: InputSignal<boolean> = input<boolean>(false);
  readonly validating: InputSignal<boolean> = input<boolean>(false);
  readonly validationResult: InputSignal<TokenValidationResult | null> = input<TokenValidationResult | null>(null);
  readonly saveError: InputSignal<string | null> = input<string | null>(null);

  readonly save: OutputEmitterRef<CreateAccountRequest | UpdateAccountRequest> = output<CreateAccountRequest | UpdateAccountRequest>();
  readonly validateToken: OutputEmitterRef<{ token: string; baseUrl: string }> = output<{ token: string; baseUrl: string }>();
  readonly cancel: OutputEmitterRef<void> = output<void>();

  @ViewChild('formHeading') readonly formHeading?: ElementRef<HTMLElement>;

  protected readonly _isEditMode: Signal<boolean> = computed(() => this.account() !== null);

  protected readonly _name: WritableSignal<string> = signal('');
  protected readonly _baseUrl: WritableSignal<string> = signal(DEFAULT_BASE_URL);
  protected readonly _token: WritableSignal<string> = signal('');
  protected readonly _showToken: WritableSignal<boolean> = signal(false);

  protected readonly _canSave: Signal<boolean> = computed(() => {
    if (this.saving()) {
      return false;
    }
    if (!this._name()) {
      return false;
    }
    if (!this._isEditMode() && !this._token()) {
      return false;
    }
    return true;
  });

  protected readonly _canValidate: Signal<boolean> = computed(() => {
    if (this.validating()) {
      return false;
    }
    return !!this._token() && !!this._baseUrl();
  });

  ngOnInit(): void {
    const acc = this.account();
    if (acc !== null) {
      this._name.set(acc.name);
      this._baseUrl.set(acc.baseUrl);
    }
  }

  onSave(): void {
    const acc = this.account();
    if (acc !== null) {
      const token = this._token() || undefined;
      const request: UpdateAccountRequest = {
        name: this._name(),
        baseUrl: this._baseUrl(),
        ...(token !== undefined ? { token } : {}),
      };
      this.save.emit(request);
    } else {
      const request: CreateAccountRequest = {
        name: this._name(),
        providerType: 'GitHub',
        baseUrl: this._baseUrl(),
        token: this._token(),
      };
      this.save.emit(request);
    }
  }
}
