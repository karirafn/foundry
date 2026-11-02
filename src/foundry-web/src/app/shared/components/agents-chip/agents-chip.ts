import { ChangeDetectionStrategy, Component, Signal, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';

/** "1 agent working" / "3 agents working", or "Agents idle" when nothing is running. */
export function agentsWorkingText(count: number): string {
  if (count === 0) {
    return 'Agents idle';
  }
  return count === 1 ? '1 agent working' : `${count} agents working`;
}

/**
 * Top-bar chip showing how many agents are working, linking to the Floor. Count changes are announced
 * through the shell's single polite announcer (see App), not a live region of its own.
 */
@Component({
  selector: 'fd-agents-chip',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './agents-chip.html',
  styleUrl: './agents-chip.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AgentsChipComponent {
  private readonly _workerSignalR = inject(WorkerSignalRService);

  protected readonly count: Signal<number> = this._workerSignalR.activeAgentCount;
  protected readonly idle: Signal<boolean> = computed(() => this.count() === 0);
  protected readonly text: Signal<string> = computed(() => agentsWorkingText(this.count()));
  protected readonly ariaLabel: Signal<string> = computed(() => `${this.text()}. Open the Floor`);
}
