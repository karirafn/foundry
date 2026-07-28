import {
  Component,
  ChangeDetectionStrategy,
  ElementRef,
  Injector,
  ViewChild,
  afterNextRender,
  afterRenderEffect,
  computed,
  inject,
  input,
  output,
  runInInjectionContext,
  signal,
} from '@angular/core';
import { LoginError, LoginPhase } from '../../../../core/models/settings.model';

interface FailedCopy {
  heading: string;
  body: string;
}

function failedCopy(error: LoginError | null): FailedCopy {
  switch (error) {
    case 'InvalidCode':
      return {
        heading: "That code didn't work",
        body: 'The code was wrong or expired. Start over and paste the new code Claude gives you.',
      };
    case 'UrlTimeout':
      return {
        heading: "Sign-in didn't start",
        body: "We couldn't reach Claude to start sign-in. Check your connection and try again.",
      };
    case 'CodeTimeout':
      return {
        heading: 'Sign-in timed out',
        body: "You didn't finish in time. Start over to get a fresh sign-in link.",
      };
    default:
      return {
        heading: 'Sign-in failed',
        body: 'Something went wrong during sign-in. Try again.',
      };
  }
}

function liveText(phase: LoginPhase, error: LoginError | null, accountEmail: string | null): string {
  switch (phase) {
    case 'Starting':
      return 'Starting sign-in…';
    case 'WaitingForAuthorization':
      return 'Sign-in link ready. Paste your code.';
    case 'SigningIn':
      return 'Signing you in…';
    case 'Succeeded':
      return accountEmail ? `Signed in as ${accountEmail}.` : 'Signed in.';
    case 'Failed':
      return failedCopy(error).heading;
    default:
      return '';
  }
}

@Component({
  selector: 'fd-oauth-login-flow',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- Persistent polite live region — always in DOM, content drives announcements -->
    <div role="status" aria-live="polite" aria-atomic="true" class="login-flow__sr-live">
      {{ _liveText() }}
    </div>

    @if (phase() === 'Starting') {
      <div class="login-flow__phase login-flow__phase--starting">
        <span class="login-flow__spinner" aria-hidden="true"></span>
        <span>Starting sign-in…</span>
      </div>
    }

    @if (phase() === 'WaitingForAuthorization') {
      <div class="login-flow__phase login-flow__phase--waiting">
        <p class="login-flow__step-label">Step 1 — Open this link to authorize</p>
        <a
          class="login-flow__url-link"
          [href]="url()"
          target="_blank"
          rel="noopener noreferrer"
          aria-label="Open Claude sign-in page in a new tab"
        >{{ url() }} <span aria-hidden="true">↗</span></a>

        <p class="login-flow__step-label">Step 2 — Paste the code from your browser</p>
        <input
          #codeInput
          class="login-flow__code-input"
          type="text"
          aria-label="Authorization code"
          aria-describedby="login-flow-code-hint"
          placeholder="Paste your code"
          autocomplete="off"
          spellcheck="false"
          autocapitalize="off"
          [value]="_codeValue()"
          (input)="_onCodeInput($event)"
          (keydown)="_onCodeKeydown($event)"
        />
        <span id="login-flow-code-hint" class="login-flow__code-hint">
          After you authorize, Claude shows a code — paste it here.
        </span>

        <div class="login-flow__actions">
          <button
            class="login-flow__submit-btn"
            type="button"
            [disabled]="!_canSubmit()"
            (click)="_onSubmit()"
          >Sign in</button>
          <button
            class="login-flow__cancel-btn"
            type="button"
            (click)="cancel.emit()"
          >Cancel</button>
        </div>
      </div>
    }

    @if (phase() === 'SigningIn') {
      <div class="login-flow__phase login-flow__phase--signing-in">
        <span class="login-flow__spinner" aria-hidden="true"></span>
        <span>Signing you in…</span>
      </div>
    }

    @if (phase() === 'Failed') {
      <div class="login-flow__phase login-flow__phase--failed">
        <p class="login-flow__error-heading">{{ _failedCopy().heading }}</p>
        <p class="login-flow__error-body">{{ _failedCopy().body }}</p>
        <button
          #retryBtn
          class="login-flow__retry-btn"
          type="button"
          (click)="retry.emit()"
        >Try again</button>
      </div>
    }
  `,
  styleUrl: './login-flow.scss',
})
export class LoginFlowComponent {
  readonly phase = input.required<LoginPhase>();
  readonly url = input<string | null>(null);
  readonly error = input<LoginError | null>(null);
  readonly accountEmail = input<string | null>(null);

  readonly submitCode = output<string>();
  readonly retry = output<void>();
  readonly cancel = output<void>();

  @ViewChild('codeInput') private readonly _codeInputRef?: ElementRef<HTMLInputElement>;
  @ViewChild('retryBtn') private readonly _retryBtnRef?: ElementRef<HTMLButtonElement>;

  private readonly _injector = inject(Injector);

  private readonly _codeValueSignal = signal('');
  protected readonly _codeValue = this._codeValueSignal.asReadonly();

  protected readonly _canSubmit = computed(() => this._codeValueSignal().trim().length > 0);

  protected readonly _liveText = computed(() => liveText(this.phase(), this.error(), this.accountEmail()));

  protected readonly _failedCopy = computed(() => failedCopy(this.error()));

  private readonly _focusEffect = afterRenderEffect(() => {
    const phase = this.phase();
    if (phase === 'WaitingForAuthorization') {
      runInInjectionContext(this._injector, () =>
        afterNextRender(() => this._codeInputRef?.nativeElement.focus())
      );
    }
    if (phase === 'Failed') {
      runInInjectionContext(this._injector, () =>
        afterNextRender(() => this._retryBtnRef?.nativeElement.focus())
      );
    }
  });

  protected _onCodeInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this._codeValueSignal.set(input.value);
  }

  protected _onCodeKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && this._canSubmit()) {
      this._onSubmit();
    }
  }

  protected _onSubmit(): void {
    const trimmed = this._codeValueSignal().trim();
    if (!trimmed) {
      return;
    }
    this._codeValueSignal.set('');
    this.submitCode.emit(trimmed);
  }
}
