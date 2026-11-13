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
import { PausedHost } from '../../../../../core/models/settings.model';

const TICK_INTERVAL_MS = 1000;

const TODAY_TIME_FORMAT = 'HH:mm';
const OTHER_DAY_TIME_FORMAT = 'MMM d, HH:mm';

interface HostPauseRow {
  readonly host: string;
  readonly pausedSince: string;
  readonly nextCheck: string;
  readonly isChecking: boolean;
  readonly failedChecks: string | null;
}

function isSameLocalDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

function describeFailedChecks(probeAttempts: number): string | null {
  if (probeAttempts < 1) {
    return null;
  }
  return probeAttempts === 1 ? '1 check failed' : `${probeAttempts} checks failed`;
}

@Component({
  selector: 'fd-host-pause-banner',
  standalone: true,
  imports: [],
  templateUrl: './host-pause-banner.html',
  styleUrl: './host-pause-banner.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [DatePipe],
})
export class HostPauseBannerComponent {
  private readonly _dispatchService = inject(DispatchService);
  private readonly _toastService = inject(ToastService);
  private readonly _datePipe = inject(DatePipe);
  private readonly _destroyRef = inject(DestroyRef);

  private readonly _tickSignal: WritableSignal<number> = signal(0);
  private _tickIntervalId: ReturnType<typeof setInterval> | null = null;

  // Effects cannot read their previous run, so the last seen host set is kept here to tell
  // newly paused hosts (announced) from hosts that left the list (toasted).
  private _previousHosts: ReadonlySet<string> | null = null;

  /** Visually-hidden one-shot announcement of newly paused hosts. */
  private readonly _announcementSignal: WritableSignal<string> = signal('');
  readonly announcement: Signal<string> = this._announcementSignal.asReadonly();

  readonly isVisible: Signal<boolean> = computed(() => this._dispatchService.pausedHosts().length > 0);

  readonly rows: Signal<readonly HostPauseRow[]> = computed(() => {
    this._tickSignal();
    const now = new Date();
    return [...this._dispatchService.pausedHosts()]
      .sort((a, b) => Date.parse(a.pausedAt) - Date.parse(b.pausedAt))
      .map((pausedHost) => this._toRow(pausedHost, now));
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
      const current = new Set(this._dispatchService.pausedHosts().map((pausedHost) => pausedHost.host));
      const previous = this._previousHosts;
      this._previousHosts = current;

      if (previous === null) {
        return;
      }

      for (const host of previous) {
        if (!current.has(host)) {
          this._toastService.show(`${host} reachable — dispatch resumed`);
        }
      }

      const added = [...current].filter((host) => !previous.has(host));
      if (added.length > 0) {
        this._announcementSignal.set(
          added.map((host) => `${host} unreachable — dispatch to this host paused`).join('. ')
        );
        // Cleared straight away so a later re-render never re-announces the same host.
        setTimeout(() => this._announcementSignal.set(''), 0);
      }
    });

    this._destroyRef.onDestroy(() => this._stopTicking());
  }

  private _toRow(pausedHost: PausedHost, now: Date): HostPauseRow {
    return {
      host: pausedHost.host,
      pausedSince: this._formatTime(pausedHost.pausedAt, now),
      nextCheck: this._formatTime(pausedHost.nextProbeAt, now),
      isChecking: Date.parse(pausedHost.nextProbeAt) <= now.getTime(),
      failedChecks: describeFailedChecks(pausedHost.probeAttempts),
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
