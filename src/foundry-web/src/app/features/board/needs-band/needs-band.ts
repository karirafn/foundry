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
import { DatePipe } from '@angular/common';
import { LifecycleBarComponent } from '../../../shared/components/lifecycle-bar/lifecycle-bar';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import { formatWaiting } from '../../../shared/utils/format-waiting';
import { BoardBandComponent } from '../board-band/board-band';
import { BoardRowComponent } from '../board-row/board-row';
import { ItemView } from '../board-state.service';
import {
  CardOpened,
  NeedsItem,
  RepoAlert,
  ReviewLink,
  compareWaiting,
  countWords,
  isCompactReview,
  reviewableLink,
} from '../board.model';
import { LifecycleRowComponent } from '../lifecycle-row/lifecycle-row';
import { RowActionComponent } from '../lifecycle-row/row-action/row-action';
import { RepoAlertComponent } from './repo-alert/repo-alert';

/**
 * The first band: repositories that hold issues, then every issue that needs a person. Issues whose
 * work is finished and only await a person's review follow the cards as one-line rows. The page
 * hides the band when empty.
 */
@Component({
  selector: 'fd-needs-band',
  imports: [
    BoardBandComponent,
    BoardRowComponent,
    DatePipe,
    LifecycleBarComponent,
    LifecycleRowComponent,
    RepoAlertComponent,
    RowActionComponent,
    SafeHrefPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './needs-band.html',
  styleUrl: './needs-band.scss',
})
export class NeedsBandComponent {
  readonly alerts: InputSignal<readonly RepoAlert[]> = input.required<readonly RepoAlert[]>();
  readonly items: InputSignal<readonly ItemView<NeedsItem>[]> =
    input.required<readonly ItemView<NeedsItem>[]>();
  readonly now: InputSignal<number> = input.required<number>();
  readonly open: OutputEmitterRef<CardOpened> = output<CardOpened>();

  protected readonly cards: Signal<readonly ItemView<NeedsItem>[]> = computed(() =>
    this.items().filter((item) => !isCompactReview(item.issue)),
  );
  protected readonly reviewRows: Signal<readonly ItemView<NeedsItem>[]> = computed(() =>
    this.items()
      .filter((item) => isCompactReview(item.issue))
      .sort(compareWaiting),
  );

  protected readonly count: Signal<number> = computed(
    () => this.alerts().length + this.items().filter((i) => i.phase === 'live').length,
  );
  protected readonly countText: Signal<string> = computed(
    () => `, ${countWords(this.count(), 'item')}`,
  );

  protected waiting(item: NeedsItem): string {
    return formatWaiting(this.now() - Date.parse(item.issue.enteredStateAt));
  }

  protected reviewLink(item: NeedsItem): ReviewLink | null {
    return reviewableLink(item.issue);
  }
}
