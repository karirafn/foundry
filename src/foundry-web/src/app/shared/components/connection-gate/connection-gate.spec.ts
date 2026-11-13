import { Component, Signal, WritableSignal, computed, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LoadOutcome } from '../../../core/models/load-outcome';
import {
  ConnectionGateService,
  GatePhase,
  GateReload,
} from '../../../core/services/connection-gate.service';
import { HubName } from '../../../core/services/hub-connection';
import {
  ConnectionGateComponent,
  GATE_RECONNECTED_MESSAGE,
  GATE_ROSE_MESSAGE,
} from './connection-gate';

class FakeGate {
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
  registered: GateReload | null = null;
  readonly register = vi.fn((reload: GateReload) => {
    this.registered = reload;
    return this.unregister;
  });
}

@Component({
  imports: [ConnectionGateComponent],
  template: `
    <fd-connection-gate [reload]="reload" [upToDate]="upToDate">
      <ng-template>
        <h1 id="page-heading" tabindex="-1">Page</h1>
        <button id="page-button" type="button">Act</button>
      </ng-template>
    </fd-connection-gate>
  `,
})
class HostComponent {
  readonly reload = vi.fn(() => Promise.resolve<LoadOutcome>('loaded'));
  readonly upToDate = (): string => 'Page is up to date.';
}

interface Harness {
  fixture: ComponentFixture<HostComponent>;
  gate: FakeGate;
  el: HTMLElement;
  render: () => Promise<void>;
}

async function setup(phase: GatePhase = 'live'): Promise<Harness> {
  const gate = new FakeGate();
  gate.phase.set(phase);
  TestBed.configureTestingModule({
    imports: [HostComponent],
    providers: [{ provide: ConnectionGateService, useValue: gate }],
  });
  const fixture = TestBed.createComponent(HostComponent);
  document.body.appendChild(fixture.nativeElement);
  const render = async (): Promise<void> => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  await render();
  return { fixture, gate, el: fixture.nativeElement as HTMLElement, render };
}

function text(el: Element | null | undefined): string {
  return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

describe('ConnectionGateComponent', () => {
  afterEach(() => {
    vi.useRealTimers();
    document.body.innerHTML = '';
  });

  it('shows the page while live and through the grace period', async () => {
    // Arrange
    const h = await setup('live');
    const live = h.el.querySelector('#page-heading');

    // Act
    h.gate.phase.set('grace');
    await h.render();

    // Assert
    expect(live).not.toBeNull();
    expect(h.el.querySelector('#page-heading')).toBe(live);
    expect(h.el.querySelector('.connection-gate')).toBeNull();
  });

  it('replaces the page with a region labelled by its heading once the gate is up', async () => {
    // Arrange
    const h = await setup('live');

    // Act
    h.gate.phase.set('reconnecting');
    await h.render();

    // Assert
    const region = h.el.querySelector('[role="region"]');
    const heading = h.el.querySelector('.connection-gate__heading');
    expect(h.el.querySelector('#page-heading')).toBeNull();
    expect(region?.getAttribute('aria-labelledby')).toBe(heading?.id);
    expect(heading?.tagName).toBe('H1');
    expect(heading?.getAttribute('tabindex')).toBe('-1');
    expect(text(heading)).toBe('Reconnecting to Foundry…');
  });

  it('counts down to the next attempt, outside any live region', async () => {
    // Arrange
    vi.useFakeTimers();
    const h = await setup('reconnecting');

    // Act
    h.gate.nextAttemptAt.set(Date.now() + 5_000);
    await h.render();
    const first = text(h.el.querySelector('.connection-gate__status'));
    await vi.advanceTimersByTimeAsync(2_000);
    await h.render();

    // Assert
    const status = h.el.querySelector('.connection-gate__status');
    expect(first).toBe('Next attempt in 5 s');
    expect(text(status)).toBe('Next attempt in 3 s');
    expect(status?.closest('[aria-live], [role="status"]')).toBeNull();
    expect(text(h.el.querySelector('.connection-gate__hint'))).toBe(
      'Live data stays hidden until the connection is back, so nothing on screen is out of date.',
    );
  });

  it('attempts at once on Try now', async () => {
    // Arrange
    const h = await setup('reconnecting');
    const button = h.el.querySelector<HTMLButtonElement>('.connection-gate__try')!;

    // Act
    button.click();

    // Assert
    expect(text(button)).toBe('Try now');
    expect(h.gate.tryNow).toHaveBeenCalledTimes(1);
  });

  it('disables Try now while an attempt is in flight', async () => {
    // Arrange / Act
    const h = await setup('trying');

    // Assert
    const button = h.el.querySelector<HTMLButtonElement>('.connection-gate__try')!;
    expect(text(h.el.querySelector('.connection-gate__status'))).toBe('Trying now…');
    expect(text(button)).toBe('Trying…');
    expect(button.disabled).toBe(true);
  });

  it('says it is loading the latest while the page reloads, without Try now', async () => {
    // Arrange / Act
    const h = await setup('reloading');

    // Assert
    expect(text(h.el.querySelector('.connection-gate__heading'))).toBe(
      'Reconnected — loading the latest…',
    );
    expect(h.el.querySelector('.connection-gate__try')).toBeNull();
    expect(h.el.querySelector('.reconnect-mark--settled')).not.toBeNull();
  });

  it('says the reload failed and when it retries, with Try now', async () => {
    // Arrange
    vi.useFakeTimers();
    const h = await setup('reload-failed');

    // Act
    h.gate.nextAttemptAt.set(Date.now() + 2_000);
    await h.render();

    // Assert
    expect(text(h.el.querySelector('.connection-gate__heading'))).toBe(
      "Reconnected, but the latest didn't load.",
    );
    expect(text(h.el.querySelector('.connection-gate__status'))).toBe('Retrying in 2 s.');
    expect(text(h.el.querySelector('.connection-gate__try'))).toBe('Try now');
  });

  it('lights one arc per connected hub', async () => {
    // Arrange
    const h = await setup('reconnecting');

    // Act
    h.gate.connectedHubs.set({ issues: true, system: false, workers: false });
    await h.render();

    // Assert
    const lit = [...h.el.querySelectorAll('.reconnect-mark__arc--lit')].map((arc) =>
      arc.getAttribute('data-hub'),
    );
    expect(lit).toEqual(['issues']);
    expect(h.el.querySelector('.reconnect-mark')?.getAttribute('aria-label')).toBe(
      '1 of 3 connections back',
    );
  });

  it('gates the page with its reload and ungates it when destroyed', async () => {
    // Arrange
    const h = await setup();

    // Act
    const outcome = await h.gate.registered!();
    h.fixture.destroy();

    // Assert
    expect(outcome).toBe('loaded');
    expect(h.fixture.componentInstance.reload).toHaveBeenCalledTimes(1);
    expect(h.gate.unregister).toHaveBeenCalledTimes(1);
  });

  it('announces the rise, the reconnect and that the page is up to date', async () => {
    // Arrange
    const h = await setup('grace');
    const announcer = (): string => text(h.el.querySelector('[aria-live="polite"]'));
    const seen: string[] = [];

    // Act
    for (const phase of ['reconnecting', 'reloading', 'live'] as const) {
      h.gate.phase.set(phase);
      await h.render();
      seen.push(announcer());
    }

    // Assert
    expect(seen).toEqual([GATE_ROSE_MESSAGE, GATE_RECONNECTED_MESSAGE, 'Page is up to date.']);
  });

  it('keeps the announcer outside the swapped content', async () => {
    // Arrange
    const h = await setup('live');
    const announcer = h.el.querySelector('[aria-live="polite"]');

    // Act
    h.gate.phase.set('reconnecting');
    await h.render();

    // Assert
    expect(h.el.querySelector('[aria-live="polite"]')).toBe(announcer);
  });

  it('moves focus from the page to the gate heading, and back to the page heading', async () => {
    // Arrange
    const h = await setup('live');
    h.el.querySelector<HTMLElement>('#page-button')!.focus();

    // Act
    h.gate.phase.set('reconnecting');
    await h.render();
    const onGate = document.activeElement;
    h.gate.phase.set('reloading');
    await h.render();
    h.gate.phase.set('live');
    await h.render();

    // Assert
    expect(onGate?.classList).toContain('connection-gate__heading');
    expect(document.activeElement?.id).toBe('page-heading');
  });

  it('leaves focus alone when it was outside the page', async () => {
    // Arrange
    const h = await setup('live');
    const outside = document.createElement('button');
    document.body.appendChild(outside);
    outside.focus();

    // Act
    h.gate.phase.set('reconnecting');
    await h.render();

    // Assert
    expect(document.activeElement).toBe(outside);
  });
});
