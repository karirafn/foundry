import { Injectable, Signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { interval, map } from 'rxjs';

const TICK_INTERVAL_MS = 30_000;

/**
 * Shared ~30s ticker. Provides a monotonically-increasing tick count as a Signal.
 * Components use this to trigger relative-time re-renders without creating individual timers.
 */
@Injectable({ providedIn: 'root' })
export class TickerService {
  readonly tick: Signal<number> = toSignal(
    interval(TICK_INTERVAL_MS).pipe(map((n) => n + 1)),
    { initialValue: 0 }
  );
}
