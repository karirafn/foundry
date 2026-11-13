import {
  DestroyRef,
  Injectable,
  Signal,
  WritableSignal,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { LoadOutcome } from '../models/load-outcome';
import { HUB_NAMES, HubConnections, HubName, SharedHubConnection } from './hub-connection';
import { IssueSignalRService } from './issue-signalr.service';
import { retryDelayMs } from './retry-schedule';
import { SystemSignalRService } from './system-signalr.service';
import { WorkerSignalRService } from './worker-signalr.service';

/**
 * Where the live pages stand with the hubs:
 * - `live`: every hub is connected and the page's data is current.
 * - `grace`: a hub has been down for under {@link GATE_GRACE_MS}; the page is left as it is.
 * - `reconnecting` / `trying`: a hub has been down longer; `trying` while an attempt is in flight.
 * - `reloading`: every hub is back and the gated page is refetching what it shows.
 * - `reload-failed`: that refetch failed; it is retried on the same backoff.
 */
export type GatePhase =
  'live' | 'grace' | 'reconnecting' | 'trying' | 'reloading' | 'reload-failed';

/** A gated page's refetch, run once every hub is back; the page stays hidden until it loads. */
export type GateReload = () => Promise<LoadOutcome>;

/** How long a hub may be down before the live pages are hidden: most blips recover within it. */
export const GATE_GRACE_MS = 2_000;
/** "Reconnected — loading the latest…" shows at least this long, so it never flashes. */
export const MIN_RELOADING_MS = 600;

type Stage = 'live' | 'grace' | 'down' | 'reloading' | 'reload-failed';

function wait(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * Combines the issues, system and workers hubs into one {@link GatePhase} for the live pages (Board
 * and Floor) and the top bar. While a gated page's data may be out of date it is not shown at all.
 */
@Injectable({ providedIn: 'root' })
export class ConnectionGateService {
  private readonly _hubs: ReadonlyMap<HubName, SharedHubConnection>;

  private readonly _stage: WritableSignal<Stage> = signal('live');
  private readonly _reloadRetryAt: WritableSignal<number | null> = signal(null);

  /** Which hubs are connected; the reconnect mark lights one arc for each. */
  readonly connectedHubs: Signal<Readonly<Record<HubName, boolean>>>;

  readonly phase: Signal<GatePhase>;

  /** The gate hides the live pages: past the grace period and not yet reloaded. */
  readonly up: Signal<boolean>;

  /** When the next reconnect or reload attempt is due, in epoch ms; null when none is waiting. */
  readonly nextAttemptAt: Signal<number | null>;

  private readonly _downSince = new Map<HubName, number>();
  private _graceTimer: ReturnType<typeof setTimeout> | null = null;
  private _reloadTimer: ReturnType<typeof setTimeout> | null = null;
  private _reload: GateReload | null = null;
  private _reloadToken = 0;
  private _reloadFailures = 0;

  constructor() {
    // Each service starts its own hub; injecting them makes sure all three are running.
    inject(IssueSignalRService);
    inject(SystemSignalRService);
    inject(WorkerSignalRService);
    const connections = inject(HubConnections);
    this._hubs = new Map(HUB_NAMES.map((hub) => [hub, connections.get(hub)]));

    this.connectedHubs = computed(() => {
      const connected = {} as Record<HubName, boolean>;
      for (const [hub, connection] of this._hubs) {
        connected[hub] = connection.status() === 'connected';
      }
      return connected;
    });

    this.phase = computed(() => {
      const stage = this._stage();
      if (stage !== 'down') {
        return stage;
      }
      return this._downHubs().some((connection) => connection.attempting())
        ? 'trying'
        : 'reconnecting';
    });

    this.up = computed(() => {
      const phase = this.phase();
      return phase !== 'live' && phase !== 'grace';
    });

    this.nextAttemptAt = computed(() => {
      const stage = this._stage();
      if (stage === 'reload-failed') {
        return this._reloadRetryAt();
      }
      if (stage !== 'down') {
        return null;
      }
      const due = this._downHubs()
        .map((connection) => connection.nextAttemptAt())
        .filter((at): at is number => at !== null);
      return due.length === 0 ? null : Math.min(...due);
    });

    effect(() => {
      const connected = this.connectedHubs();
      untracked(() => this._onHubs(connected));
    });

    inject(DestroyRef).onDestroy(() => {
      this._clearGraceTimer();
      this._cancelReload();
    });
  }

  /**
   * Gates a page: once the hubs are back after the gate rose, `reload` runs and the page shows
   * only when it loads. Returns the function that ungates it.
   */
  register(reload: GateReload): () => void {
    this._reload = reload;
    const stage = this._stage();
    if (stage === 'reloading' || stage === 'reload-failed') {
      this._startReload();
    }
    return () => {
      if (this._reload !== reload) {
        return;
      }
      this._reload = null;
      const current = this._stage();
      if (current === 'reloading' || current === 'reload-failed') {
        this._cancelReload();
        this._stage.set('live');
      }
    };
  }

  /** Cuts the wait short: every down hub attempts now, or a failed reload runs again. */
  tryNow(): void {
    const stage = this._stage();
    if (stage === 'down') {
      for (const connection of this._downHubs()) {
        connection.tryNow();
      }
      return;
    }
    if (stage === 'reload-failed') {
      this._startReload();
    }
  }

  private _downHubs(): SharedHubConnection[] {
    return [...this._hubs.values()].filter((connection) => connection.status() !== 'connected');
  }

  private _onHubs(connected: Readonly<Record<HubName, boolean>>): void {
    const now = Date.now();
    for (const hub of HUB_NAMES) {
      if (connected[hub]) {
        this._downSince.delete(hub);
      } else if (!this._downSince.has(hub)) {
        this._downSince.set(hub, now);
      }
    }

    const stage = this._stage();
    if (this._downSince.size === 0) {
      this._clearGraceTimer();
      if (stage === 'down') {
        this._reloadFailures = 0;
        this._startReload();
      } else if (stage === 'grace') {
        this._stage.set('live');
      }
      return;
    }

    if (stage === 'live' || stage === 'grace') {
      this._stage.set('grace');
      this._checkGrace();
    } else if (stage === 'reloading' || stage === 'reload-failed') {
      this._cancelReload();
      this._stage.set('down');
    }
  }

  // The gate rises once any one hub has been down for the whole grace period.
  private _checkGrace(): void {
    this._clearGraceTimer();
    const longestDownSince = Math.min(...this._downSince.values());
    const remaining = longestDownSince + GATE_GRACE_MS - Date.now();
    if (remaining <= 0) {
      this._stage.set('down');
      return;
    }
    this._graceTimer = setTimeout(() => {
      this._graceTimer = null;
      if (this._stage() === 'grace' && this._downSince.size > 0) {
        this._checkGrace();
      }
    }, remaining);
  }

  private _startReload(): void {
    this._cancelReload();
    const reload = this._reload;
    if (reload === null) {
      this._stage.set('live');
      return;
    }
    this._stage.set('reloading');
    const token = this._reloadToken;
    void Promise.all([reload(), wait(MIN_RELOADING_MS)]).then(([outcome]) => {
      if (token !== this._reloadToken) {
        return;
      }
      if (outcome === 'loaded') {
        this._reloadFailures = 0;
        this._stage.set('live');
        return;
      }
      this._reloadFailures += 1;
      this._scheduleReload(retryDelayMs(this._reloadFailures));
    });
  }

  private _scheduleReload(delayMs: number): void {
    this._stage.set('reload-failed');
    this._reloadRetryAt.set(Date.now() + delayMs);
    this._reloadTimer = setTimeout(() => {
      this._reloadTimer = null;
      this._startReload();
    }, delayMs);
  }

  // A reload in flight is abandoned: its result no longer applies.
  private _cancelReload(): void {
    this._reloadToken += 1;
    this._reloadRetryAt.set(null);
    if (this._reloadTimer !== null) {
      clearTimeout(this._reloadTimer);
      this._reloadTimer = null;
    }
  }

  private _clearGraceTimer(): void {
    if (this._graceTimer !== null) {
      clearTimeout(this._graceTimer);
      this._graceTimer = null;
    }
  }
}
