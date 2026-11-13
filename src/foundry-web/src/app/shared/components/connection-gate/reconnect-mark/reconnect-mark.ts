import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  computed,
  input,
} from '@angular/core';
import { HUB_NAMES, HubName } from '../../../../core/services/hub-connection';

interface Arc {
  readonly hub: HubName;
  readonly path: string;
  readonly lit: boolean;
  readonly delayMs: number;
}

// The arcs share a base at (60, 92) and open upward, 45° either side of vertical.
const BASE_X = 60;
const BASE_Y = 92;
const RADII = [26, 48, 70];
const HALF_ANGLE = Math.PI / 4;
// Down arcs sweep in turn, inner first.
const STAGGER_MS = 150;

function arcPath(radius: number): string {
  const dx = Math.round(radius * Math.sin(HALF_ANGLE) * 100) / 100;
  const dy = Math.round(radius * Math.cos(HALF_ANGLE) * 100) / 100;
  return `M ${BASE_X - dx} ${BASE_Y - dy} A ${radius} ${radius} 0 0 1 ${BASE_X + dx} ${BASE_Y - dy}`;
}

/**
 * Three concentric arcs, one per hub (issues innermost, then system, then workers). A lit arc is
 * connected; the others sweep while they reconnect. `settled` holds them lit and pulses a ring from
 * the base while the page reloads. Reduced motion shows the arcs still and no ring.
 */
@Component({
  selector: 'fd-reconnect-mark',
  templateUrl: './reconnect-mark.html',
  styleUrl: './reconnect-mark.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReconnectMarkComponent {
  readonly connected: InputSignal<Readonly<Record<HubName, boolean>>> =
    input.required<Readonly<Record<HubName, boolean>>>();
  /** Every hub is back and the page is reloading. */
  readonly settled: InputSignal<boolean> = input(false);

  protected readonly baseX = BASE_X;
  protected readonly baseY = BASE_Y;
  protected readonly ringRadius = RADII[RADII.length - 1];

  protected readonly arcs: Signal<readonly Arc[]> = computed(() => {
    const connected = this.connected();
    return HUB_NAMES.map((hub, i) => ({
      hub,
      path: arcPath(RADII[i]),
      lit: connected[hub],
      delayMs: i * STAGGER_MS,
    }));
  });

  protected readonly label: Signal<string> = computed(() => {
    const back = this.arcs().filter((arc) => arc.lit).length;
    return `${back} of ${HUB_NAMES.length} connections back`;
  });
}
