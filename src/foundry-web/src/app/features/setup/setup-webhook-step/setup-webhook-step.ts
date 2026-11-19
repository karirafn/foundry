import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  InputSignal,
  OutputEmitterRef,
  Signal,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { SAVE_WEBHOOKS_ERROR, SettingsService } from '../../../core/services/settings.service';
import { SpinnerComponent } from '../../../shared/components/spinner/spinner';
import { WebhookExposeGuideComponent } from '../../settings/dispatch/webhook-expose-guide/webhook-expose-guide';
import {
  validateWebhookPublicBaseUrl,
  webhookUrlClientErrorMessage,
  webhookUrlHost,
} from '../../settings/dispatch/webhook-url';

/** The server's default backstop interval; the wizard sends it only when none is stored, so it never changes it. */
const BACKSTOP_INTERVAL_DEFAULT = 15;
const WEBHOOK_URL_ERROR_CODES: readonly string[] = ['Settings.InvalidWebhookPublicBaseUrl', 'Settings.WebhookPublicBaseUrlUnreachable'];
const GENERAL_ERROR = 'Couldn\'t save the webhook URL. Please try again.';

/** Setup wizard step that stores the Webhook Public Base URL once the server has verified it reaches this Foundry (ADR 0116). */
@Component({
  selector: 'fd-setup-webhook-step',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SpinnerComponent, WebhookExposeGuideComponent],
  template: `
    <div
      class="setup-webhook-step"
      role="region"
      aria-labelledby="setup-webhook-title"
      [attr.aria-busy]="_pending() ? 'true' : null"
    >
      <h2 id="setup-webhook-title" class="setup-webhook-step__title" tabindex="-1">Expose Webhooks</h2>
      <p class="setup-webhook-step__description">
        GitHub and GitLab notify Foundry through webhooks, so it reacts to issues and pull requests within seconds.
        Make Foundry's webhook port reachable on a public HTTPS address, then enter that address below — Foundry
        checks it before continuing.
      </p>

      @if (_port(); as port) {
        <div class="setup-webhook-step__port">
          <span class="setup-webhook-step__field-label" id="setup-webhook-port-label">Webhook port</span>
          <code class="setup-webhook-step__port-value" aria-labelledby="setup-webhook-port-label">{{ port }}</code>
          <span class="setup-webhook-step__hint">Plain HTTP, serving only /webhooks/*. Expose it through a tunnel or reverse proxy that terminates TLS.</span>
        </div>

        <fd-webhook-expose-guide [port]="port" />

        <div class="setup-webhook-step__field">
          <label class="setup-webhook-step__field-label" for="setup-webhook-url">Webhook public base URL</label>
          <input
            #urlInput
            class="setup-webhook-step__input"
            id="setup-webhook-url"
            type="url"
            inputmode="url"
            autocomplete="off"
            spellcheck="false"
            required
            placeholder="https://hooks.example.com"
            [value]="_url()"
            [readonly]="_pending()"
            [attr.aria-invalid]="_fieldError() ? 'true' : null"
            aria-describedby="setup-webhook-url-hint setup-webhook-url-error"
            (input)="_url.set($any($event.target).value)"
          />
          <span id="setup-webhook-url-hint" class="setup-webhook-step__hint">The public https address your tunnel or proxy serves, without /webhooks — for example https://hooks.example.com.</span>
          <span id="setup-webhook-url-error" class="setup-webhook-step__field-error">{{ _fieldError() ?? '' }}</span>
        </div>

        <div id="setup-webhook-status" class="setup-webhook-step__status" role="status">{{ _probeStatus() }}</div>
        <div class="setup-webhook-step__save-error" role="alert">{{ _generalError() ?? '' }}</div>
      } @else {
        <div class="setup-webhook-step__load-error" role="alert">Couldn't load Foundry's settings, so the webhook port is unknown.</div>
      }

      <div class="setup-webhook-step__actions">
        @if (showBack()) {
          <button
            class="setup-webhook-step__back-btn"
            type="button"
            [disabled]="_pending()"
            (click)="back.emit()"
          >Back</button>
        }
        @if (_port() !== null) {
          <button
            class="setup-webhook-step__continue-btn"
            type="button"
            [attr.aria-disabled]="_pending() ? 'true' : null"
            (click)="onContinue()"
          >
            @if (_pending()) {
              <fd-spinner />
            }
            {{ _continueLabel() }}
          </button>
        } @else {
          <button
            class="setup-webhook-step__retry-btn"
            type="button"
            (click)="onRetry()"
          >Retry</button>
        }
      </div>
    </div>
  `,
  styleUrl: './setup-webhook-step.scss',
})
export class SetupWebhookStepComponent {
  private readonly _settingsService = inject(SettingsService);
  private readonly _injector = inject(Injector);

  readonly showBack: InputSignal<boolean> = input<boolean>(true);

  readonly complete: OutputEmitterRef<void> = output<void>();
  readonly back: OutputEmitterRef<void> = output<void>();

  protected readonly _url: WritableSignal<string> = signal('');

  /** Set by the first submit that fails the client check; from then on the field re-validates on every input. */
  private readonly _clientChecked: WritableSignal<boolean> = signal(false);

  /** The trimmed URL of the request in flight or last answered; null before any request. */
  private readonly _submittedUrl: WritableSignal<string | null> = signal(null);

  private readonly _urlInput = viewChild<ElementRef<HTMLInputElement>>('urlInput');

  private _urlInitialized = false;

  protected readonly _port: Signal<number | null> = computed(() => this._settingsService.settings()?.webhookPort ?? null);

  private readonly _storedUrl: Signal<string> = computed(() => this._settingsService.settings()?.webhookPublicBaseUrl ?? '');

  protected readonly _pending: Signal<boolean> = computed(
    () => this._submittedUrl() !== null && this._settingsService.savingWebhooks(),
  );

  private readonly _urlUnchanged: Signal<boolean> = computed(
    () => this._storedUrl() !== '' && this._url().trim() === this._storedUrl(),
  );

  protected readonly _continueLabel: Signal<string> = computed(() => {
    if (this._pending()) {
      return 'Verifying…';
    }
    return this._urlUnchanged() ? 'Continue' : 'Verify and continue';
  });

  private readonly _answeredErrorCode: Signal<string | null> = computed(() =>
    this._submittedUrl() === null || this._pending() ? null : this._settingsService.saveWebhooksErrorCode(),
  );

  private readonly _failedOnUrl: Signal<boolean> = computed(() => WEBHOOK_URL_ERROR_CODES.includes(this._answeredErrorCode() ?? ''));

  protected readonly _fieldError: Signal<string | null> = computed(() => {
    if (this._clientChecked()) {
      const kind = validateWebhookPublicBaseUrl(this._url());
      if (kind !== null) {
        return webhookUrlClientErrorMessage(kind);
      }
    }
    if (!this._failedOnUrl() || this._url().trim() !== this._submittedUrl()) {
      return null;
    }
    return this._settingsService.saveWebhooksError();
  });

  protected readonly _generalError: Signal<string | null> = computed(() => {
    if (this._submittedUrl() === null || this._pending() || this._failedOnUrl()) {
      return null;
    }
    const error = this._settingsService.saveWebhooksError();
    if (error === null) {
      return null;
    }
    return error === SAVE_WEBHOOKS_ERROR ? GENERAL_ERROR : error;
  });

  protected readonly _probeStatus: Signal<string> = computed(() => {
    const submitted = this._submittedUrl();
    return this._pending() && submitted !== null
      ? `Checking that ${webhookUrlHost(submitted)} reaches this Foundry. This can take up to 10 seconds.`
      : '';
  });

  constructor() {
    effect(() => {
      const settings = this._settingsService.settings();
      if (settings !== null && !this._urlInitialized) {
        this._urlInitialized = true;
        untracked(() => this._url.set(settings.webhookPublicBaseUrl ?? ''));
      }
    });

    effect(() => {
      const succeeded =
        this._submittedUrl() !== null && !this._settingsService.savingWebhooks() && this._settingsService.saveWebhooksSuccess();
      if (succeeded) {
        untracked(() => {
          this._submittedUrl.set(null);
          this.complete.emit();
        });
      }
    });

    // A URL error is announced by moving focus to the field; its message span is not a live region.
    effect(() => {
      if (this._failedOnUrl()) {
        this._focusInput();
      }
    });
  }

  onContinue(): void {
    if (this._pending()) {
      return;
    }
    if (validateWebhookPublicBaseUrl(this._url()) !== null) {
      this._clientChecked.set(true);
      this._focusInput();
      return;
    }
    if (this._urlUnchanged()) {
      this.complete.emit();
      return;
    }
    const url = this._url().trim();
    this._submittedUrl.set(url);
    this._settingsService.updateWebhookSettings(
      url,
      this._settingsService.settings()?.backstopIntervalMinutes ?? BACKSTOP_INTERVAL_DEFAULT,
    );
  }

  onRetry(): void {
    this._settingsService.loadSettings();
  }

  private _focusInput(): void {
    afterNextRender(() => this._urlInput()?.nativeElement.focus(), { injector: this._injector });
  }
}
