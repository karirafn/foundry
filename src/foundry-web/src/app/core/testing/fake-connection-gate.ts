import { Provider, Signal, WritableSignal, computed, signal } from '@angular/core';
import { vi } from 'vitest';
import { ConnectionGateService, GatePhase, GateReload } from '../services/connection-gate.service';
import { HubName } from '../services/hub-connection';

/** Stands in for the connection gate so a spec sets its phase by hand. */
export class FakeConnectionGate {
  readonly phase: WritableSignal<GatePhase> = signal('live');
  readonly up: Signal<boolean> = computed(
    () => this.phase() !== 'live' && this.phase() !== 'grace',
  );
  readonly connectedHubs: WritableSignal<Record<HubName, boolean>> = signal({
    issues: true,
    system: true,
    workers: true,
  });
  readonly nextAttemptAt: WritableSignal<number | null> = signal(null);
  readonly tryNow = vi.fn();
  readonly unregister = vi.fn();
  /** The reload of the page gated last. */
  registered: GateReload | null = null;
  readonly register = vi.fn((reload: GateReload) => {
    this.registered = reload;
    return this.unregister;
  });
}

export function provideFakeConnectionGate(
  gate: FakeConnectionGate = new FakeConnectionGate(),
): Provider {
  return { provide: ConnectionGateService, useValue: gate };
}
