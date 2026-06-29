import { Component, DestroyRef, Signal, computed, effect, inject, signal } from '@angular/core';
import { DispatchService } from '../../../../../core/services/dispatch.service';
import { ToastService } from '../../../../../core/services/toast.service';

const USAGE_LIMIT_RESET_MESSAGE = 'Usage limit reset';

const COUNTDOWN_INTERVAL_MS = 1000;

@Component({
  selector: 'fd-dispatch-banner',
  standalone: true,
  imports: [],
  templateUrl: './dispatch-banner.html',
  styleUrl: './dispatch-banner.scss',
})
export class DispatchBannerComponent {
  private readonly _dispatchService = inject(DispatchService);
  private readonly _toastService = inject(ToastService);
  private readonly _destroyRef = inject(DestroyRef);

  // Mutable cross-effect state: effects cannot read their own previous signal values,
  // so we track whether we were counting down to detect the zero-crossing.
  private _wasCountingDown = false;

  private readonly _tickSignal = signal(0);

  readonly remainingMs: Signal<number | null> = computed(() => {
    this._tickSignal();
    const resetsAt = this._dispatchService.usageLimitResetsAt();
    if (resetsAt === null) {
      return null;
    }
    return new Date(resetsAt).getTime() - Date.now();
  });

  readonly isUsageLimitActive: Signal<boolean> = computed(() => {
    const ms = this.remainingMs();
    return ms !== null && ms > 0;
  });

  readonly isDispatchBannerVisible: Signal<boolean> = computed(
    () => this._dispatchService.isDispatchPaused() || this.isUsageLimitActive()
  );

  readonly countdownText: Signal<string | null> = computed(() => {
    const ms = this.remainingMs();
    if (ms === null || ms <= 0) {
      return null;
    }
    return this._formatCountdown(ms);
  });

  constructor() {
    effect(() => {
      const current = this.remainingMs();

      if (current === null) {
        this._wasCountingDown = false;
        return;
      }

      if (current > 0) {
        this._wasCountingDown = true;
        return;
      }

      if (this._wasCountingDown) {
        this._toastService.show(USAGE_LIMIT_RESET_MESSAGE);
        this._wasCountingDown = false;
      }
    });

    const intervalId = setInterval(() => {
      if (this._dispatchService.usageLimitResetsAt() !== null) {
        this._tickSignal.update((n) => n + 1);
      }
    }, COUNTDOWN_INTERVAL_MS);

    this._destroyRef.onDestroy(() => clearInterval(intervalId));
  }

  private _formatCountdown(remainingMs: number): string {
    const totalSeconds = Math.ceil(remainingMs / 1000);
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;

    if (hours >= 1) {
      return `${hours}h ${minutes}m`;
    }

    if (totalSeconds >= 60) {
      return `${minutes}m ${seconds}s`;
    }

    return `${seconds}s`;
  }
}
