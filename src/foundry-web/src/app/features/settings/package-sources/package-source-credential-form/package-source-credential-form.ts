import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  InputSignal,
  OnInit,
  OutputEmitterRef,
  Signal,
  ViewChild,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { SpinnerComponent } from '../../../../shared/components/spinner/spinner';
import {
  CredentialField,
  CredentialFieldErrors,
  PackageSource,
  SaveCredentialRequest,
  validateCredential,
} from '../package-source.model';

const FIELD_ORDER: readonly CredentialField[] = ['username', 'token'];

@Component({
  selector: 'fd-package-source-credential-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SpinnerComponent],
  template: `
    <div
      class="package-source-credential-form"
      role="region"
      aria-label="Package source credential form"
      [attr.aria-busy]="saving() ? 'true' : null"
    >
      <button
        class="package-source-credential-form__cancel-link"
        type="button"
        [disabled]="saving()"
        (click)="cancel.emit()"
      >
        <span aria-hidden="true">←</span> Cancel
      </button>

      <h2 class="package-source-credential-form__heading" #formHeading tabindex="-1">
        {{ _isReplace() ? 'Replace credential' : 'Add credential' }}
      </h2>

      <div class="package-source-credential-form__field">
        <span class="package-source-credential-form__label">Package source</span>
        <span class="package-source-credential-form__url">{{ source().url }}</span>
        <span class="package-source-credential-form__hint">
          Covers every repository that declares this source ({{ source().repositories.length }}).
        </span>
      </div>

      <div
        class="package-source-credential-form__summary"
        role="alert"
      >{{ _summary() }}</div>

      <div class="package-source-credential-form__field">
        <label class="package-source-credential-form__label" for="pscf-username">Username</label>
        <input
          class="package-source-credential-form__input"
          type="text"
          id="pscf-username"
          autocomplete="off"
          [value]="_username()"
          [attr.aria-invalid]="_errors().username ? 'true' : null"
          aria-describedby="pscf-username-hint pscf-username-error"
          (input)="onInput('username', $any($event.target).value)"
        />
        <span id="pscf-username-hint" class="package-source-credential-form__hint">
          The username Foundry sends with the token.
        </span>
        <span id="pscf-username-error" class="package-source-credential-form__error" role="alert">{{ _errors().username ?? '' }}</span>
      </div>

      <div class="package-source-credential-form__field">
        <label class="package-source-credential-form__label" for="pscf-token">
          {{ _isReplace() ? 'New token' : 'Token' }}
        </label>
        <div class="package-source-credential-form__token-wrapper">
          <input
            class="package-source-credential-form__input"
            [type]="_showToken() ? 'text' : 'password'"
            id="pscf-token"
            autocomplete="new-password"
            [value]="_token()"
            [attr.aria-invalid]="_errors().token ? 'true' : null"
            aria-describedby="pscf-token-hint pscf-token-error"
            (input)="onInput('token', $any($event.target).value)"
          />
          <button
            class="package-source-credential-form__toggle-visibility-btn"
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
        <span id="pscf-token-hint" class="package-source-credential-form__hint">
          {{ _isReplace()
            ? 'The stored token is never shown. Enter it again to replace the credential.'
            : "Needs read access to this source's packages." }}
        </span>
        <span id="pscf-token-error" class="package-source-credential-form__error" role="alert">{{ _errors().token ?? '' }}</span>
      </div>

      <div class="package-source-credential-form__save-error" role="alert">{{ saveError() ?? '' }}</div>

      <button
        class="package-source-credential-form__save-btn"
        type="button"
        [attr.aria-disabled]="saving() ? 'true' : null"
        (click)="onSave()"
      >
        @if (saving()) {
          <fd-spinner />
          Saving…
        } @else {
          Save credential
        }
      </button>
    </div>
  `,
  styleUrl: './package-source-credential-form.scss',
})
export class PackageSourceCredentialFormComponent implements OnInit {
  private readonly _injector = inject(Injector);
  private readonly _host: ElementRef<HTMLElement> = inject(ElementRef);

  readonly source: InputSignal<PackageSource> = input.required<PackageSource>();
  readonly saving: InputSignal<boolean> = input<boolean>(false);
  readonly fieldErrors: InputSignal<CredentialFieldErrors> = input<CredentialFieldErrors>({});
  readonly saveError: InputSignal<string | null> = input<string | null>(null);

  readonly save: OutputEmitterRef<SaveCredentialRequest> = output<SaveCredentialRequest>();
  readonly cancel: OutputEmitterRef<void> = output<void>();
  /** The operator edited a field, so its server error no longer applies. */
  readonly fieldEdited: OutputEmitterRef<CredentialField> = output<CredentialField>();

  @ViewChild('formHeading') readonly formHeading?: ElementRef<HTMLElement>;

  protected readonly _username: WritableSignal<string> = signal('');
  protected readonly _token: WritableSignal<string> = signal('');
  protected readonly _showToken: WritableSignal<boolean> = signal(false);
  private readonly _clientErrors: WritableSignal<CredentialFieldErrors> = signal({});

  protected readonly _isReplace: Signal<boolean> = computed(() => this.source().credential !== null);

  /** Client errors win; a server error shows for a field the client found nothing wrong with. */
  protected readonly _errors: Signal<CredentialFieldErrors> = computed(() => ({
    ...this.fieldErrors(),
    ...this._clientErrors(),
  }));

  protected readonly _summary: Signal<string> = computed(() => {
    const count = FIELD_ORDER.filter((f) => this._errors()[f] !== undefined).length;
    if (count === 0) {
      return '';
    }
    return count === 1 ? '1 field needs attention.' : `${count} fields need attention.`;
  });

  constructor() {
    // A server 400 moves focus to the first invalid field, like a client validation failure.
    effect(() => {
      const serverErrors = this.fieldErrors();
      if (serverErrors.username !== undefined || serverErrors.token !== undefined) {
        untracked(() => this._focusFirstInvalid());
      }
    });
  }

  ngOnInit(): void {
    this._username.set(this.source().credential?.username ?? '');
  }

  onInput(field: CredentialField, value: string): void {
    if (field === 'username') {
      this._username.set(value);
    } else {
      this._token.set(value);
    }
    this._clientErrors.update((errors) => {
      if (errors[field] === undefined) {
        return errors;
      }
      const next = { ...errors };
      delete next[field];
      return next;
    });
    if (this.fieldErrors()[field] !== undefined) {
      this.fieldEdited.emit(field);
    }
  }

  onSave(): void {
    if (this.saving()) {
      return;
    }
    const request: SaveCredentialRequest = { username: this._username(), token: this._token() };
    const errors = validateCredential(request);
    this._clientErrors.set(errors);
    if (Object.keys(errors).length > 0) {
      this._focusFirstInvalid();
      return;
    }
    this.save.emit(request);
  }

  private _focusFirstInvalid(): void {
    afterNextRender(
      () => this._host.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(),
      { injector: this._injector },
    );
  }
}
