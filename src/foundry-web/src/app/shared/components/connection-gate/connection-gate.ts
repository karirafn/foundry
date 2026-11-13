import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  InputSignal,
  OnInit,
  Signal,
  TemplateRef,
  WritableSignal,
  afterNextRender,
  computed,
  contentChild,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { DOCUMENT, NgTemplateOutlet } from '@angular/common';
import {
  ConnectionGateService,
  GatePhase,
  GateReload,
} from '../../../core/services/connection-gate.service';
import { ReconnectMarkComponent } from './reconnect-mark/reconnect-mark';

export const GATE_ROSE_MESSAGE = 'Connection lost. Reconnecting to Foundry.';
export const GATE_RECONNECTED_MESSAGE = 'Reconnected. Loading the latest.';

const COUNTDOWN_TICK_MS = 1_000;

let nextId = 0;

/**
 * Wraps a live page (its content in an `ng-template`) and replaces it with a reconnect screen while
 * the hubs are down, and until the page has reloaded once they are back. The page is destroyed while
 * the screen shows and renders afresh after, so nothing on screen is ever out of date.
 */
@Component({
  selector: 'fd-connection-gate',
  imports: [NgTemplateOutlet, ReconnectMarkComponent],
  templateUrl: './connection-gate.html',
  styleUrl: './connection-gate.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConnectionGateComponent implements OnInit {
  /** The page's refetch, run once every hub is back. */
  readonly reload: InputSignal<GateReload> = input.required<GateReload>();
  /** What to announce once the page is back and current, e.g. "Floor is up to date." */
  readonly upToDate: InputSignal<() => string> = input.required<() => string>();

  protected readonly content: Signal<TemplateRef<unknown>> = contentChild.required(TemplateRef);
  private readonly _heading = viewChild<ElementRef<HTMLElement>>('gateHeading');

  protected readonly gate = inject(ConnectionGateService);
  private readonly _host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly _document = inject(DOCUMENT);
  private readonly _injector = inject(Injector);
  private readonly _destroyRef = inject(DestroyRef);

  protected readonly headingId = `fd-connection-gate-heading-${nextId++}`;
  protected readonly phase: Signal<GatePhase> = this.gate.phase;
  protected readonly showContent: Signal<boolean> = computed(() => !this.gate.up());

  protected readonly heading: Signal<string> = computed(() => {
    switch (this.phase()) {
      case 'reloading':
        return 'Reconnected — loading the latest…';
      case 'reload-failed':
        return "Reconnected, but the latest didn't load.";
      default:
        return 'Reconnecting to Foundry…';
    }
  });

  private readonly _now: WritableSignal<number> = signal(Date.now());
  /** Whole seconds until the next attempt, rounded up. */
  protected readonly seconds: Signal<number> = computed(() => {
    const at = this.gate.nextAttemptAt();
    return at === null ? 0 : Math.max(0, Math.ceil((at - this._now()) / 1000));
  });

  private readonly _announcement: WritableSignal<string> = signal('');
  protected readonly announcement: Signal<string> = this._announcement.asReadonly();

  // Focus that was inside the gate when its content was swapped out is put back on a heading.
  private _focusInside = false;

  constructor() {
    this._trackFocus();
    this._runCountdown();
    this._announcePhases();
    this._keepFocus();
  }

  ngOnInit(): void {
    const unregister = this.gate.register(() => this.reload()());
    this._destroyRef.onDestroy(unregister);
  }

  private _trackFocus(): void {
    const active = this._document.activeElement;
    this._focusInside = active !== null && this._host.contains(active);
    const onFocusIn = (event: FocusEvent): void => {
      this._focusInside = event.target instanceof Node && this._host.contains(event.target);
    };
    this._document.addEventListener('focusin', onFocusIn);
    this._destroyRef.onDestroy(() => this._document.removeEventListener('focusin', onFocusIn));
  }

  // The countdown recomputes from nextAttemptAt each second, so a throttled background tab catches up.
  private _runCountdown(): void {
    let handle: ReturnType<typeof setInterval> | null = null;
    const stop = (): void => {
      if (handle !== null) {
        clearInterval(handle);
        handle = null;
      }
    };
    effect(() => {
      const at = this.gate.nextAttemptAt();
      untracked(() => {
        stop();
        this._now.set(Date.now());
        if (at !== null) {
          handle = setInterval(() => this._now.set(Date.now()), COUNTDOWN_TICK_MS);
        }
      });
    });
    this._destroyRef.onDestroy(stop);
  }

  private _announcePhases(): void {
    let previous: GatePhase | null = null;
    effect(() => {
      const phase = this.phase();
      const before = previous;
      previous = phase;
      untracked(() => {
        const message = this._messageFor(before, phase);
        if (message !== null) {
          this._announcement.set(message);
        }
      });
    });
  }

  private _messageFor(before: GatePhase | null, phase: GatePhase): string | null {
    const wasUp = before !== null && before !== 'live' && before !== 'grace';
    const wasDown = before === 'reconnecting' || before === 'trying';
    const down = phase === 'reconnecting' || phase === 'trying';
    if (down && !wasDown) {
      return GATE_ROSE_MESSAGE;
    }
    if (phase === 'reloading' && wasDown) {
      return GATE_RECONNECTED_MESSAGE;
    }
    if (phase === 'live' && wasUp) {
      return this.upToDate()();
    }
    return null;
  }

  private _keepFocus(): void {
    effect(() => {
      const showing = this.showContent();
      void this.phase();
      afterNextRender(
        () => {
          const active = this._document.activeElement;
          const lost = active === null || active === this._document.body;
          if (!this._focusInside || !lost) {
            return;
          }
          const target = showing
            ? this._host.querySelector<HTMLElement>('h1')
            : (this._heading()?.nativeElement ?? null);
          target?.focus();
        },
        { injector: this._injector },
      );
    });
  }
}
