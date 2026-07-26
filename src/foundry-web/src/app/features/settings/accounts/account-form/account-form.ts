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
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import {
  AccountSummary,
  CreateAccountRequest,
  ProviderType,
  TokenRequirements,
  TokenValidationResult,
  UpdateAccountRequest,
} from '../account.model';
import { ProviderSelectorComponent } from '../provider-selector/provider-selector';
import { AccountService } from '../account.service';

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
            (providerChange)="onProviderChange($event)"
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

      @if (_tokenRequirements(); as req) {
        <section class="account-form__requirements" aria-labelledby="account-form-req-heading">
          <h3 id="account-form-req-heading" class="account-form__requirements-heading">
            <svg class="account-form__requirements-icon" aria-hidden="true" xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <circle cx="12" cy="12" r="10"></circle>
              <line x1="12" y1="16" x2="12" y2="12"></line>
              <line x1="12" y1="8" x2="12.01" y2="8"></line>
            </svg>
            Token requirements
          </h3>
          <p class="account-form__requirements-text">
            Create a <strong>{{ req.tokenTypeLabel }}</strong> with the following
            scope{{ req.scopes.length === 1 ? '' : 's' }}:
          </p>
          <ul class="account-form__requirements-scopes" aria-label="Required scopes">
            @for (scope of req.scopes; track scope) {
              <li class="account-form__requirements-scope"><code>{{ scope }}</code></li>
            }
          </ul>
          @if (_createTokenUrl(); as url) {
            <a class="account-form__requirements-link" [href]="url" target="_blank" rel="noopener noreferrer">
              Create token on {{ _createTokenHost() }}
              <span class="account-form__requirements-link-icon" aria-hidden="true">↗</span>
              <span class="sr-only">(opens in a new tab)</span>
            </a>
          } @else {
            <p class="account-form__requirements-link-hint">
              Enter a Base URL above to get a direct link to the token creation page.
            </p>
          }
        </section>
      }

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
          <span class="account-form__validation-block">
            <span class="account-form__validation-message">Resolving identity…</span>
          </span>
        } @else if (_resultVisible() && validationResult(); as result) {
          @if (result.isValid && result.accountName) {
            <span class="account-form__validation-block">
              <span class="account-form__validation-dot account-form__validation-dot--valid" aria-hidden="true"></span>
              <span class="account-form__validation-message account-form__validation-message--valid">
                Authenticated as <span class="account-form__account-name">{{ result.accountName }}</span>
              </span>
            </span>
          } @else if (result.isAuthFailure) {
            <span class="account-form__validation-block">
              <span class="account-form__validation-dot account-form__validation-dot--error" aria-hidden="true"></span>
              <span class="account-form__validation-message account-form__validation-message--error">
                Authentication failed — check that the token is correct
              </span>
            </span>
          } @else if (!result.isValid && result.missingScopes.length > 0) {
            <span class="account-form__validation-block">
              <span class="account-form__validation-dot account-form__validation-dot--warning" aria-hidden="true"></span>
              <span class="account-form__validation-message account-form__validation-message--warning">
                Missing required scopes: {{ result.missingScopes.join(', ') }}
              </span>
            </span>
          } @else if (result.isValid && !result.accountName) {
            <span class="account-form__validation-block">
              <span class="account-form__validation-dot account-form__validation-dot--error" aria-hidden="true"></span>
              <span class="account-form__validation-message account-form__validation-message--error">
                Token is valid, but the account identity could not be resolved from the provider
              </span>
            </span>
          }
        }
        @if (_isDuplicate()) {
          <span class="account-form__validation-block">
            <span class="account-form__validation-dot account-form__validation-dot--warning" aria-hidden="true"></span>
            <span class="account-form__validation-message account-form__validation-message--warning">
              An account for "{{ _resolvedAccountName() }}" already exists on {{ _resolvedHost() }}
            </span>
          </span>
        }
        @if (_isEditMode() && _showRenameNotice()) {
          <span class="account-form__validation-block">
            <span class="account-form__validation-dot account-form__validation-dot--warning" aria-hidden="true"></span>
            <span class="account-form__validation-message account-form__validation-message--warning">
              Saving will rename this account to "{{ _resolvedAccountName() }}"
            </span>
          </span>
        }
      </div>

      <div
        id="account-form-validation-error"
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
  private readonly _accountService: AccountService = inject(AccountService);

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
  protected readonly _tokenRequirements: WritableSignal<TokenRequirements | null> = signal(null);

  /** Tracks the last (token, baseUrl) pair that was sent to resolution to avoid duplicate calls. */
  private readonly _lastResolvedPair: WritableSignal<{ token: string; baseUrl: string } | null> = signal(null);

  /** Whether a held resolution is pending (token present but baseUrl was empty at blur time). */
  private readonly _pendingResolution: WritableSignal<boolean> = signal(false);

  /** Resolved name from the current (non-stale) validation result; null when the result is stale. */
  protected readonly _resolvedAccountName: Signal<string | null> = computed(() => {
    const last = this._lastResolvedPair();
    if (!last || last.token !== this._token() || last.baseUrl !== this._baseUrl()) {
      return null;
    }
    return this.validationResult()?.accountName ?? null;
  });

  protected readonly _resolvedHost: Signal<string> = computed(() => {
    try {
      return new URL(this._baseUrl()).host;
    } catch {
      return this._baseUrl();
    }
  });

  protected readonly _createTokenUrl: Signal<string | null> = computed(() => {
    const requirements = this._tokenRequirements();
    if (!requirements) {
      return null;
    }
    const template = requirements.creationUrlTemplate;
    if (!template.includes('{baseUrl}')) {
      return template;
    }
    const rawBaseUrl = this._baseUrl().trim();
    if (!rawBaseUrl) {
      return null;
    }
    try {
      const parsed = new URL(rawBaseUrl);
      if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
        return null;
      }
      const baseUrl = rawBaseUrl.replace(/\/$/, '');
      return template.replace('{baseUrl}', baseUrl);
    } catch {
      return null;
    }
  });

  protected readonly _createTokenHost: Signal<string> = computed(() => {
    return this._resolvedHost();
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

  /** True only when the last resolved pair still matches current inputs — hides stale results after edits. */
  protected readonly _resultVisible: Signal<boolean> = computed(() => {
    const last = this._lastResolvedPair();
    if (!last) {
      return false;
    }
    return last.token === this._token() && last.baseUrl === this._baseUrl();
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
      if (!this._resultVisible()) {
        return false;
      }
      const result = this.validationResult();
      return result !== null && result.isValid && !!result.accountName;
    }
    if (!this._resultVisible()) {
      return false;
    }
    const result = this.validationResult();
    if (!result || !result.isValid || !result.accountName) {
      return false;
    }
    return !!this._token();
  });

  protected readonly _tokenAriaDescribedBy: Signal<string> = computed(() => {
    const parts: string[] = ['account-token-validation', 'account-form-validation-error', 'account-token-error'];
    if (this._isEditMode()) {
      parts.unshift('account-form-token-hint');
      if (this.account()?.hasToken) {
        parts.unshift('account-form-token-on-file');
      }
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
      const provider = this._normalizeProviderType(acc.providerType);
      if (provider !== null) {
        this._fetchTokenRequirements(provider);
      }
    } else {
      this._fetchTokenRequirements(this._provider());
    }
  }

  private _normalizeProviderType(raw: string): ProviderType | null {
    const lower = raw.toLowerCase();
    if (lower === 'github') {
      return 'GitHub';
    }
    if (lower === 'gitlab') {
      return 'GitLab';
    }
    return null;
  }

  private _fetchTokenRequirements(provider: ProviderType): void {
    this._accountService.getTokenRequirements(provider).then(
      (requirements) => { this._tokenRequirements.set(requirements); },
      () => { this._tokenRequirements.set(null); }
    );
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

  onProviderChange(provider: ProviderType): void {
    this._provider.set(provider);
    this._tokenRequirements.set(null);
    this._fetchTokenRequirements(provider);
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
