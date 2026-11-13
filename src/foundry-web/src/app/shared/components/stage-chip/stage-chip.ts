import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  computed,
  input,
} from '@angular/core';

/**
 * Where an issue stands in the flow, as a sentence-case label with one tiny pip per step; the
 * current step's pip is lit. It knows nothing about issue states. Screen readers hear "Stage: <label>".
 */
@Component({
  selector: 'fd-stage-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="stage-chip"
    ><span class="stage-chip__pips" aria-hidden="true">
      @for (pip of pips(); track $index) {
        <span class="stage-chip__pip" [attr.data-pip]="pip"></span>
      }</span
    ><span class="sr-only">Stage: </span>{{ label() }}</span
  >`,
  styleUrl: './stage-chip.scss',
})
export class StageChipComponent {
  readonly label: InputSignal<string> = input.required<string>();
  /** 1-based position of this stage in the flow. */
  readonly step: InputSignal<number> = input.required<number>();
  readonly steps: InputSignal<number> = input.required<number>();

  protected readonly pips: Signal<readonly ('past' | 'current' | 'next')[]> = computed(() =>
    Array.from({ length: this.steps() }, (_, i) => {
      if (i + 1 < this.step()) {
        return 'past';
      }
      return i + 1 === this.step() ? 'current' : 'next';
    }),
  );
}
