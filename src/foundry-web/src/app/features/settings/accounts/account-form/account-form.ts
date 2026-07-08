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
  afterNextRender,
  computed,
  input,
  output,
  signal,
} from '@angular/core';
import {
  AccountSummary,
  CreateAccountRequest,
  ProviderType,
  TokenValidationResult,
  UpdateAccountRequest,
} from '../account.model';
import { ProviderSelectorComponent } from '../provider-selector/provider-selector';

const GITHUB_BASE_URL = 'https://github.com';

@Component({
  selector: 'fd-account-form',
  standalone: true,
  imports: [ProviderSelectorComponent],
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

      @if (_isEditMode()) {
        <div class="account-form__field">
          <span class="account-form__field-label">Provider</span>
          <span class="account-form__provider-badge">{{ account()!.providerType }}</span>
        </div>
      } @else {
        <div class="account-form__field">
          <span id="account-form-provider-label" class="account-form__field-label">Provider</span>
          <fd-provider-selector
            [provider]="_provider()"
            (providerChange)="_provider.set($event)"
            (defaultBaseUrlChange)="onDefaultBaseUrlChange($event)"
            [ariaLabelledBy]="'account-form-provider-label'"
          />
        </div>
      }

      <div class="account-form__field">
        <label class="account-form__field-label" for="account-form-base-url">Base URL</label>
        <input
          class="account-form__input"
          type="text"
          id="account-form-base-url"
          [value]="_baseUrl()"
          (input)="onBaseUrlInput($any($event.target).value)"
          (blur)="onBaseUrlBlur()"
          autocomplete="off"
        />
      </div>

      @if (_isEditMode() && account()!.hasToken) {
        <div
          id="account-form-token-on-file"
          class="account-form__token-on-file"
        >
          <span class="account-form__validation-dot account-form__validation-dot--valid" aria-hidden="true"></span>
          <span class="account-form__validation-message account-form__validation-message--valid">
            Token on file — authenticated as {{ account()!.name }}
          </span>
        </div>
      }

      <div class="account-form__field">
        <label class="account-form__field-label" for="account-form-token">
          {{ _isEditMode() ? 'Replace token' : 'Token' }}
        </label>
        <div
          class="account-form__token-wrapper"
          [attr.aria-busy]="validating() ? true : null"
        >
          <input
            class="account-form__input"
            [type]="_showToken() ? 'text' : 'password'"
            id="account-form-token"
            [value]="_token()"
            (input)="onTokenInput($any($event.target).value)"
            (blur)="onTokenBlur()"
            (paste)="onTokenPaste()"
            autocomplete="off"
            [required]="!_isEditMode()"
            [attr.aria-required]="!_isEditMode() || null"
            [attr.aria-describedby]="_tokenAriaDescribedBy()"
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
        </div>

        @if (_isEditMode()) {
          <span id="account-form-token-hint" class="account-form__field-hint">
            Leave empty to keep the current token
          </span>
        }
      </div>

      <div
        id="account-token-validation"
        class="account-form__validation-result"
        role="status"
        aria-live="polite"
      >
        @if (validating()) {
          <span class="account-form__validation-message">Resolving identity…</span>
        } @else if (validationResult(); as result) {
          @if (result.isValid && result.accountName) {
            <span class="account-form__validation-dot account-form__validation-dot--valid" aria-hidden="true"></span>
            <span class="account-form__validation-message account-form__validation-message--valid">
              Authenticated as <span class="account-form__account-name">{{ result.accountName }}</span>
            </span>
          } @else if (result.isAuthFailure) {
            <span class="account-form__validation-dot account-form__validation-dot--error" aria-hidden="true"></span>
            <span class="account-form__validation-message account-form__validation-message--error">
              Authentication failed — check that the token is correct
            </span>
          } @else if (!result.isValid && result.missingScopes.length > 0) {
            <span class="account-form__validation-dot account-form__validation-dot--warning" aria-hidden="true"></span>
            <span class="account-form__validation-message account-form__validation-message--warning">
              Missing required scopes: {{ result.missingScopes.join(', ') }}
            </span>
          } @else if (result.isValid && !result.accountName) {
            <span class="account-form__validation-dot account-form__validation-dot--error" aria-hidden="true"></span>
            <span class="account-form__validation-message account-form__validation-message--error">
              Token is valid, but the account identity could not be resolved from the provider
            </span>
          }
        }
      </div>

      @if (_isDuplicate()) {
        <div class="account-form__duplicate-warning">
          <span class="account-form__validation-dot account-form__validation-dot--warning" aria-hidden="true"></span>
          <span class="account-form__validation-message account-form__validation-message--warning">
            An account for "{{ _resolvedAccountName() }}" already exists on {{ _resolvedHost() }}
          </span>
        </div>
      }

      @if (_isEditMode() && _showRenameNotice()) {
        <div class="account-form__rename-notice">
          <span class="account-form__validation-dot account-form__validation-dot--warning" aria-hidden="true"></span>
          <span class="account-form__validation-message account-form__validation-message--warning">
            Saving will rename this account to "{{ _resolvedAccountName() }}"
          </span>
        </div>
      }

      <div
        class="account-form__validation-error"
        role="alert"
      >{{ validationError() ?? '' }}</div>

      <div
        id="account-token-error"
        class="account-form__save-error"
        role="alert"
      >{{ saveError() ?? '' }}</div>

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
  readonly accounts: InputSignal<AccountSummary[]> = input<AccountSummary[]>([]);
  readonly saving: InputSignal<boolean> = input<boolean>(false);
  readonly validating: InputSignal<boolean> = input<boolean>(false);
  readonly validationResult: InputSignal<TokenValidationResult | null> = input<TokenValidationResult | null>(null);
  readonly saveError: InputSignal<string | null> = input<string | null>(null);
  readonly validationError: InputSignal<string | null> = input<string | null>(null);

  readonly save: OutputEmitterRef<CreateAccountRequest | UpdateAccountRequest> = output<CreateAccountRequest | UpdateAccountRequest>();
  readonly validateToken: OutputEmitterRef<{ token: string; baseUrl: string }> = output<{ token: string; baseUrl: string }>();
  readonly cancel: OutputEmitterRef<void> = output<void>();

  @ViewChild('formHeading') readonly formHeading?: ElementRef<HTMLElement>;

  protected readonly _isEditMode: Signal<boolean> = computed(() => this.account() !== null);

  protected readonly _provider: WritableSignal<ProviderType> = signal('GitHub');
  protected readonly _baseUrl: WritableSignal<string> = signal(GITHUB_BASE_URL);
  protected readonly _baseUrlManuallyEdited: WritableSignal<boolean> = signal(false);
  protected readonly _token: WritableSignal<string> = signal('');
  protected readonly _showToken: WritableSignal<boolean> = signal(false);

  /** Tracks the last (token, baseUrl) pair that was sent to resolution to avoid duplicate calls. */
  private readonly _lastResolvedPair: WritableSignal<{ token: string; baseUrl: string } | null> = signal(null);

  /** Whether a held resolution is pending (token present but baseUrl was empty at blur time). */
  private readonly _pendingResolution: WritableSignal<boolean> = signal(false);

  protected readonly _resolvedAccountName: Signal<string | null> = computed(() =>
    this.validationResult()?.accountName ?? null
  );

  protected readonly _resolvedHost: Signal<string> = computed(() => {
    try {
      return new URL(this._baseUrl()).host;
    } catch {
      return this._baseUrl();
    }
  });

  protected readonly _isDuplicate: Signal<boolean> = computed(() => {
    const name = this._resolvedAccountName();
    const baseUrl = this._baseUrl().trim().toLowerCase();
    if (!name) {
      return false;
    }
    const editingId = this.account()?.id ?? null;
    return this.accounts().some(a => {
      if (editingId !== null && a.id === editingId) {
        return false;
      }
      return a.name.toLowerCase() === name.toLowerCase() &&
        a.baseUrl.trim().toLowerCase() === baseUrl;
    });
  });

  protected readonly _showRenameNotice: Signal<boolean> = computed(() => {
    const acc = this.account();
    if (!acc) {
      return false;
    }
    const resolved = this._resolvedAccountName();
    if (!resolved) {
      return false;
    }
    return resolved !== acc.name;
  });

  protected readonly _canSave: Signal<boolean> = computed(() => {
    if (this.saving()) {
      return false;
    }
    if (this._isDuplicate()) {
      return false;
    }
    if (this._isEditMode()) {
      const hasNewToken = !!this._token();
      if (!hasNewToken) {
        return true;
      }
      const result = this.validationResult();
      return result !== null && result.isValid && !!result.accountName;
    }
    const result = this.validationResult();
    if (!result || !result.isValid || !result.accountName) {
      return false;
    }
    return !!this._token();
  });

  protected readonly _tokenAriaDescribedBy: Signal<string> = computed(() => {
    const parts = ['account-form-token-hint', 'account-token-validation', 'account-token-error'];
    if (this._isEditMode() && this.account()?.hasToken) {
      parts.unshift('account-form-token-on-file');
    }
    return parts.join(' ');
  });

  constructor() {
    afterNextRender(() => {
      this.formHeading?.nativeElement.focus();
    });
  }

  ngOnInit(): void {
    const acc = this.account();
    if (acc !== null) {
      this._baseUrl.set(acc.baseUrl);
    }
  }

  onBaseUrlInput(value: string): void {
    this._baseUrl.set(value);
    this._baseUrlManuallyEdited.set(true);
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

  private _triggerResolution(): void {
    const token = this._token();
    const baseUrl = this._baseUrl();
    const last = this._lastResolvedPair();
    if (last && last.token === token && last.baseUrl === baseUrl) {
      return;
    }
    this._lastResolvedPair.set({ token, baseUrl });
    this.validateToken.emit({ token, baseUrl });
  }

  onSave(): void {
    const acc = this.account();
    if (acc !== null) {
      const token = this._token() || undefined;
      const request: UpdateAccountRequest = {
        baseUrl: this._baseUrl(),
        ...(token !== undefined ? { token } : {}),
      };
      this.save.emit(request);
    } else {
      const request: CreateAccountRequest = {
        providerType: this._provider(),
        baseUrl: this._baseUrl(),
        token: this._token(),
      };
      this.save.emit(request);
    }
  }
}
