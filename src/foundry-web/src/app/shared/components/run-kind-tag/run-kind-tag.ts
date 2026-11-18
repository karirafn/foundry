import { ChangeDetectionStrategy, Component, InputSignal, Signal, computed, input } from '@angular/core';
import { RUN_KIND_LABELS, RunKind } from '../../utils/run-kind';

/** An issue's Run Kind as a small outlined, lower-case mono tag; screen readers hear "Run: <label>". */
@Component({
  selector: 'fd-run-kind-tag',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="run-kind-tag"><span class="sr-only">Run: </span>{{ label() }}</span>`,
  styleUrl: './run-kind-tag.scss',
})
export class RunKindTagComponent {
  readonly kind: InputSignal<RunKind> = input.required<RunKind>();

  protected readonly label: Signal<string> = computed(() => RUN_KIND_LABELS[this.kind()]);
}
