import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  OutputEmitterRef,
  WritableSignal,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { RoleTagComponent } from '../../../shared/components/role-tag/role-tag';
import { BoardBandComponent } from '../board-band/board-band';
import { BoardRowComponent } from '../board-row/board-row';
import { ItemView } from '../board-state.service';
import { BlockedItem, CardOpened } from '../board.model';

/** Where the disclosure state lives; anything but `'true'` means collapsed. */
export const BLOCKED_OPEN_KEY = 'fd.board.blocked-open';

/** Issues waiting on blockers, behind a disclosure that is collapsed by default. */
@Component({
  selector: 'fd-blocked-band',
  imports: [BoardBandComponent, BoardRowComponent, RoleTagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './blocked-band.html',
  styleUrl: './blocked-band.scss',
})
export class BlockedBandComponent {
  readonly items: InputSignal<readonly ItemView<BlockedItem>[]> =
    input.required<readonly ItemView<BlockedItem>[]>();
  readonly open: OutputEmitterRef<CardOpened> = output<CardOpened>();

  private readonly _storage: Storage | null = inject(DOCUMENT).defaultView?.localStorage ?? null;
  protected readonly expanded: WritableSignal<boolean> = signal(this._readExpanded());

  protected toggle(): void {
    const next = !this.expanded();
    this.expanded.set(next);
    try {
      this._storage?.setItem(BLOCKED_OPEN_KEY, String(next));
    } catch {
      // Storage full or denied: the disclosure still works for this visit.
    }
  }

  private _readExpanded(): boolean {
    try {
      return this._storage?.getItem(BLOCKED_OPEN_KEY) === 'true';
    } catch {
      return false;
    }
  }
}
