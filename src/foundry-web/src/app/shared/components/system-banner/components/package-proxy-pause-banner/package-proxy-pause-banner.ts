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
import { DatePipe } from '@angular/common';
import { DispatchService } from '../../../../../core/services/dispatch.service';
import { ToastService } from '../../../../../core/services/toast.service';
import { PackageProxyPause } from '../../../../../core/models/settings.model';
import { describeFailedChecks, isSameLocalDay } from '../host-pause-banner/host-pause-banner';

const TICK_INTERVAL_MS = 1000;

const TODAY_TIME_FORMAT = 'HH:mm';
const OTHER_DAY_TIME_FORMAT = 'MMM d, HH:mm';

const PAUSED_ANNOUNCEMENT = 'Package proxy unreachable — dispatch paused for repositories with package sources';

// Not "reachable": Resume all clears the pause too, and that proves nothing about the proxy.
const CLEARED_TOAST = 'Package proxy pause cleared — dispatch resumed';

interface PackageProxyPauseView {
  readonly pausedSince: string;
  readonly nextCheck: string;
  readonly isChecking: boolean;
  readonly failedChecks: string | null;
}

@Component({
  selector: 'fd-package-proxy-pause-banner',
  standalone: true,
  imports: [],
  templateUrl: './package-proxy-pause-banner.html',
  styleUrl: './package-proxy-pause-banner.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [DatePipe],
})
export class PackageProxyPauseBannerComponent {
  private readonly _dispatchService = inject(DispatchService);
  private readonly _toastService = inject(ToastService);
  private readonly _datePipe = inject(DatePipe);
  private readonly _destroyRef = inject(DestroyRef);

  private readonly _tickSignal: WritableSignal<number> = signal(0);
  private _tickIntervalId: ReturnType<typeof setInterval> | null = null;

  // Effects cannot read their previous run, so the last seen state is kept here to tell a new pause (announced)
  // from a cleared one (toasted). Undefined until the first run, which neither toasts nor announces.
  private _wasPaused: boolean | undefined = undefined;

  /** Visually-hidden one-shot announcement of a new pause. */
  private readonly _announcementSignal: WritableSignal<string> = signal('');
  readonly announcement: Signal<string> = this._announcementSignal.asReadonly();

  readonly isVisible: Signal<boolean> = computed(() => this._dispatchService.packageProxyPause() !== null);

  readonly view: Signal<PackageProxyPauseView | null> = computed(() => {
    this._tickSignal();
    const pause = this._dispatchService.packageProxyPause();
    return pause === null ? null : this._toView(pause, new Date());
  });

  constructor() {
    effect(() => {
      if (this.isVisible()) {
        this._startTicking();
      } else {
        this._stopTicking();
      }
    });

    effect(() => {
      const isPaused = this.isVisible();
      const wasPaused = this._wasPaused;
      this._wasPaused = isPaused;

      if (wasPaused === undefined || wasPaused === isPaused) {
        return;
      }

      if (!isPaused) {
        this._toastService.show(CLEARED_TOAST);
        return;
      }

      this._announcementSignal.set(PAUSED_ANNOUNCEMENT);
      // Cleared straight away so a later re-render never re-announces the same pause.
      setTimeout(() => this._announcementSignal.set(''), 0);
    });

    this._destroyRef.onDestroy(() => this._stopTicking());
  }

  private _toView(pause: PackageProxyPause, now: Date): PackageProxyPauseView {
    return {
      pausedSince: this._formatTime(pause.pausedAt, now),
      nextCheck: this._formatTime(pause.nextProbeAt, now),
      isChecking: Date.parse(pause.nextProbeAt) <= now.getTime(),
      failedChecks: describeFailedChecks(pause.probeAttempts),
    };
  }

  private _formatTime(iso: string, now: Date): string {
    const format = isSameLocalDay(new Date(iso), now) ? TODAY_TIME_FORMAT : OTHER_DAY_TIME_FORMAT;
    return this._datePipe.transform(iso, format) ?? '';
  }

  private _startTicking(): void {
    if (this._tickIntervalId !== null) {
      return;
    }
    this._tickIntervalId = setInterval(() => this._tickSignal.update((n) => n + 1), TICK_INTERVAL_MS);
  }

  private _stopTicking(): void {
    if (this._tickIntervalId === null) {
      return;
    }
    clearInterval(this._tickIntervalId);
    this._tickIntervalId = null;
  }
}
