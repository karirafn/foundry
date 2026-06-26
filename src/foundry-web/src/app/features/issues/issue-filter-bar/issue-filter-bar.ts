import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Signal,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { IssueService } from '../issue.service';
import { IssueFilterSheetComponent } from '../issue-filter-sheet/issue-filter-sheet';

@Component({
  selector: 'fd-issue-filter-bar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IssueFilterSheetComponent],
  template: `
    <div class="filter-bar">
      <button
        #filterBtn
        type="button"
        class="filter-bar__btn"
        aria-haspopup="dialog"
        [attr.aria-expanded]="_sheetOpen()"
        [attr.aria-label]="filterBtnAriaLabel()"
        (click)="openSheet()"
      >
        Filter
        @if (issueService.activeFilterCount() > 0) {
          <span class="filter-bar__badge" aria-hidden="true">{{ issueService.activeFilterCount() }}</span>
        }
      </button>
      <fd-issue-filter-sheet
        #sheet
        [open]="_sheetOpen()"
        [triggerRef]="_filterBtnEl()"
        (close)="closeSheet()"
      />
    </div>
  `,
  styleUrl: './issue-filter-bar.scss',
})
export class IssueFilterBarComponent {
  protected readonly issueService = inject(IssueService);

  protected readonly _sheetOpen = signal(false);

  protected readonly _filterBtnEl = viewChild.required<ElementRef<HTMLButtonElement>>('filterBtn');
  protected readonly _sheetRef = viewChild<IssueFilterSheetComponent>('sheet');

  protected readonly filterBtnAriaLabel: Signal<string> = computed(() => {
    const count = this.issueService.activeFilterCount();
    return count > 0 ? `Filter, ${count} filters active` : 'Filter';
  });

  protected openSheet(): void {
    this._sheetOpen.set(true);
  }

  protected closeSheet(): void {
    this._sheetOpen.set(false);
  }
}
