import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  OutputEmitterRef,
  Signal,
  WritableSignal,
  computed,
  input,
  output,
  signal,
} from '@angular/core';
import { LifecycleBarComponent } from '../../../shared/components/lifecycle-bar/lifecycle-bar';
import { stepCopy } from '../../../shared/components/lifecycle-rail/lifecycle-steps';
import { RUN_KIND_LABELS } from '../../../shared/utils/run-kind';
import { currentStep } from '../../issues/lifecycle.model';
import { BoardBandComponent } from '../board-band/board-band';
import { BoardRowComponent } from '../board-row/board-row';
import { ItemView } from '../board-state.service';
import { CardOpened, PipelineItem } from '../board.model';

/** Rows shown before Show all. */
export const PIPELINE_PREVIEW = 10;

/** Every issue waiting for dispatch, as one list in global dispatch order. */
@Component({
  selector: 'fd-pipeline-band',
  imports: [BoardBandComponent, BoardRowComponent, LifecycleBarComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './pipeline-band.html',
  styleUrl: './pipeline-band.scss',
})
export class PipelineBandComponent {
  readonly items: InputSignal<readonly ItemView<PipelineItem>[]> =
    input.required<readonly ItemView<PipelineItem>[]>();
  /** The last queue-order reconcile failed, so positions may be out of date. */
  readonly orderStale: InputSignal<boolean> = input<boolean>(false);
  readonly open: OutputEmitterRef<CardOpened> = output<CardOpened>();

  protected readonly preview = PIPELINE_PREVIEW;
  // Not persisted: the full list is a look, not a setting.
  protected readonly showAll: WritableSignal<boolean> = signal(false);

  protected readonly shown: Signal<readonly ItemView<PipelineItem>[]> = computed(() =>
    this.showAll() ? this.items() : this.items().slice(0, PIPELINE_PREVIEW),
  );

  protected isNext(item: PipelineItem): boolean {
    return item.slot.kind === 'position' && item.slot.position === 1;
  }

  /** The marker's full text, for its title when the fixed marker column ellipsises it. */
  protected markerText(item: PipelineItem): string {
    switch (item.slot.kind) {
      case 'detected':
        return 'Detected';
      case 'ineligible':
        return 'Repo ineligible';
      default:
        return this.phrase(item);
    }
  }

  /** The row's one status phrase: the step it waits on, and the round past the first. */
  protected phrase(item: PipelineItem): string {
    const current = currentStep(item.issue.lifecycle);
    const step =
      current !== null
        ? stepCopy(current.step).label
        : item.runKind !== null
          ? `Queued: ${RUN_KIND_LABELS[item.runKind]}`
          : 'Queued';
    return item.issue.round > 1 ? `${step} · round ${item.issue.round}` : step;
  }
}
