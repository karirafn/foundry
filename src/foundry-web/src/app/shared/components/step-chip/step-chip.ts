import { ChangeDetectionStrategy, Component, InputSignal, input } from '@angular/core';

export type StepChipTone = 'working' | 'warning' | 'waiting';

/**
 * A step Foundry is running, as a pill; it knows nothing about issue states. The working and warning
 * tones carry a sweeping sheen and a pulsing dot; the waiting tone stays still, as nothing is running.
 */
@Component({
  selector: 'fd-step-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span
    class="step-chip"
    [class.step-chip--warning]="tone() === 'warning'"
    [class.step-chip--waiting]="tone() === 'waiting'"
    ><span class="step-chip__dot" aria-hidden="true"></span>{{ label() }}</span
  >`,
  styleUrl: './step-chip.scss',
})
export class StepChipComponent {
  readonly label: InputSignal<string> = input.required<string>();
  readonly tone: InputSignal<StepChipTone> = input<StepChipTone>('working');
}
