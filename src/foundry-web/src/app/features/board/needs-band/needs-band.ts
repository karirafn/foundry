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
import { BoardBandComponent } from '../board-band/board-band';
import { ItemView } from '../board-state.service';
import { CardOpened, NeedsItem, RepoAlert, countWords } from '../board.model';
import { LifecycleRowComponent } from '../lifecycle-row/lifecycle-row';
import { RowActionComponent } from '../lifecycle-row/row-action/row-action';
import { RepoAlertComponent } from './repo-alert/repo-alert';

/** The first band: repositories that hold issues, then every issue that needs a person. The page hides it when empty. */
@Component({
  selector: 'fd-needs-band',
  imports: [BoardBandComponent, LifecycleRowComponent, RepoAlertComponent, RowActionComponent],
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

  protected readonly count: Signal<number> = computed(
    () => this.alerts().length + this.items().filter((i) => i.phase === 'live').length,
  );
  protected readonly countText: Signal<string> = computed(
    () => `, ${countWords(this.count(), 'item')}`,
  );
}
