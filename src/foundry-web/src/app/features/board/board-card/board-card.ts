import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, Signal, computed, input, output } from '@angular/core';
import { RoleTagComponent } from '../../../shared/components/role-tag/role-tag';
import { StepChipComponent } from '../../../shared/components/step-chip/step-chip';
import { CODE_STEP_LABELS, codeStepTone } from '../board.model';
import { BoardCardView } from '../board-state.service';

type CardTone = 'running' | 'error' | 'warning' | 'ready' | 'neutral' | 'completed';

/** A minimal board card: a button that opens the issue's detail pane. */
@Component({
  selector: 'fd-board-card',
  imports: [RoleTagComponent, StepChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.data-issue-card]': 'card().issue.id',
    '[attr.data-needs]': "card().cell === 'needs' && live()",
    '[style.view-transition-name]': "'fd-card-' + card().issue.id",
    '[style.view-transition-class]': "'fd-card'",
  },
  templateUrl: './board-card.html',
  styleUrl: './board-card.scss',
})
export class BoardCardComponent {
  readonly card: InputSignal<BoardCardView> = input.required<BoardCardView>();
  /** Emits the card's button, so focus can return to it when the pane closes. */
  readonly open: OutputEmitterRef<HTMLElement> = output<HTMLElement>();

  protected readonly live: Signal<boolean> = computed(() => this.card().phase === 'live');

  protected readonly tone: Signal<CardTone | null> = computed(() => {
    const card = this.card();
    if (card.phase !== 'live') {
      return 'completed';
    }
    if (card.reason !== null) {
      return card.reason.tone;
    }
    return card.running ? 'running' : null;
  });

  protected readonly step: Signal<{ label: string; tone: 'working' | 'warning' } | null> = computed(() => {
    const codeStep = this.card().issue.codeStep;
    return codeStep === null ? null : { label: CODE_STEP_LABELS[codeStep], tone: codeStepTone(codeStep) };
  });

  protected readonly hasMarkers: Signal<boolean> = computed(() => {
    const card = this.card();
    return (
      card.nextRole !== null || this.step() !== null || card.reason !== null || card.ineligible || card.queueMarker !== null
    );
  });
}
