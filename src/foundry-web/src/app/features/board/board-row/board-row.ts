import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  OutputEmitterRef,
  Signal,
  computed,
  input,
  output,
} from '@angular/core';
import { IssueSummary } from '../../issues/issue.model';
import { CardPhase } from '../board-state.service';
import { IssueRefComponent } from '../issue-ref/issue-ref';

/**
 * A compact one-line issue row: a projected `[rowLead]` (position and role, or a step chip), the
 * repository and number, the title as the row's one button, and a projected `[rowMarker]`. Used by
 * the Pipeline, Blocked and step rows.
 */
@Component({
  selector: 'fd-board-row',
  imports: [IssueRefComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.data-issue-id]': 'issue().id',
    '[style.view-transition-name]': "'fd-card-' + issue().id",
    '[style.view-transition-class]': "'fd-card'",
  },
  templateUrl: './board-row.html',
  styleUrl: './board-row.scss',
})
export class BoardRowComponent {
  readonly issue: InputSignal<IssueSummary> = input.required<IssueSummary>();
  /** The next issue to dispatch: tinted, with a leading rule. */
  readonly next: InputSignal<boolean> = input<boolean>(false);
  readonly phase: InputSignal<CardPhase> = input<CardPhase>('live');
  readonly moved: InputSignal<boolean> = input<boolean>(false);
  /** Emits the row's button, so focus can return to it when the pane closes. */
  readonly open: OutputEmitterRef<HTMLElement> = output<HTMLElement>();

  protected readonly idBase: Signal<string> = computed(() => `board-row-${this.issue().id}`);
  protected readonly label: Signal<string> = computed(
    () => `${this.issue().title}, #${this.issue().issueNumber}`,
  );
  protected readonly describedBy: Signal<string> = computed(() => {
    const base = this.idBase();
    return `${base}-lead ${base}-repo ${base}-marker`;
  });
}
