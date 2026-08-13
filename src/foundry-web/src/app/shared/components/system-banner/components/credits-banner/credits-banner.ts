import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Signal,
  WritableSignal,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { CreditsService } from '../../../../../core/services/credits.service';
import { formatCountdown } from '../../../../utils/format-countdown';

const COUNTDOWN_INTERVAL_MS = 1000;

type ViewState = 'counting-down' | 'checking';

@Component({
  selector: 'fd-credits-banner',
  standalone: true,
  imports: [],
  templateUrl: './credits-banner.html',
  styleUrl: './credits-banner.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CreditsBannerComponent {
  protected readonly creditsService = inject(CreditsService);
  private readonly _destroyRef = inject(DestroyRef);

  private readonly _tickSignal: WritableSignal<number> = signal(0);

  /** Visually-hidden one-shot announcement text for state transitions. */
  private readonly _transitionAlertTextSignal: WritableSignal<string> = signal('');
  readonly transitionAlertText: Signal<string> = this._transitionAlertTextSignal.asReadonly();

  private _previousState: ViewState | null = null;

  readonly isVisible: Signal<boolean> = computed(() => this.creditsService.nextProbeAt() !== null);

  readonly remainingMs: Signal<number> = computed(() => {
    this._tickSignal();
    const nextProbeAt = this.creditsService.nextProbeAt();
    if (nextProbeAt === null) {
      return 0;
    }
    return new Date(nextProbeAt).getTime() - Date.now();
  });

  readonly countdownText: Signal<string> = computed(() => {
    const ms = this.remainingMs();
    if (ms <= 0) {
      return '0s';
    }
    return formatCountdown(ms);
  });

  readonly viewState: Signal<ViewState> = computed(() => {
    if (this.creditsService.isChecking()) {
      return 'checking';
    }
    return 'counting-down';
  });

  constructor() {
    effect(() => {
      const state = this.viewState();
      const prev = this._previousState;
      this._previousState = state;

      if (prev === null) {
        return;
      }

      if (state === 'checking' && prev !== 'checking') {
        this._transitionAlertTextSignal.set('Checking whether the Claude account can spend again');
        setTimeout(() => this._transitionAlertTextSignal.set(''), 0);
      } else if (state === 'counting-down' && prev === 'checking') {
        this._transitionAlertTextSignal.set('Automatic check failed — next check soon');
        setTimeout(() => this._transitionAlertTextSignal.set(''), 0);
      }
    });

    const intervalId = setInterval(() => {
      if (this.creditsService.nextProbeAt() !== null) {
        this._tickSignal.update((n) => n + 1);
      }
    }, COUNTDOWN_INTERVAL_MS);

    this._destroyRef.onDestroy(() => clearInterval(intervalId));
  }
}
