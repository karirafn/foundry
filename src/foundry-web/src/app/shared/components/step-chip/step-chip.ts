import { ChangeDetectionStrategy, Component, InputSignal, input } from '@angular/core';

export type StepChipTone = 'working' | 'warning';

/** A step Foundry is running, as a pill with a sweeping sheen; it knows nothing about issue states. */
@Component({
  selector: 'fd-step-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="step-chip" [class.step-chip--warning]="tone() === 'warning'"
    ><span class="step-chip__dot" aria-hidden="true"></span>{{ label() }}</span
  >`,
  styleUrl: './step-chip.scss',
})
export class StepChipComponent {
  readonly label: InputSignal<string> = input.required<string>();
  readonly tone: InputSignal<StepChipTone> = input<StepChipTone>('working');
}
