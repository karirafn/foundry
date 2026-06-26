import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  OutputEmitterRef,
  afterRenderEffect,
  inject,
  input,
  output,
} from '@angular/core';
import { IssueFilterRailComponent } from '../issue-filter-rail/issue-filter-rail';

const HEADING_ID = 'filter-sheet-heading';
const FOCUSABLE_SELECTOR = 'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

@Component({
  selector: 'fd-issue-filter-sheet',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IssueFilterRailComponent],
  template: `
    @if (open()) {
      <div
        class="filter-sheet__scrim"
        aria-hidden="true"
        (click)="onScrimClick()"
      ></div>
      <div
        class="filter-sheet__panel"
        role="dialog"
        aria-modal="true"
        [attr.aria-labelledby]="headingId"
      >
        <div class="filter-sheet__header">
          <div class="filter-sheet__drag-handle" aria-hidden="true"></div>
          <h2
            class="filter-sheet__title"
            [id]="headingId"
          >Filter issues</h2>
          <button
            type="button"
            class="filter-sheet__close-btn"
            aria-label="Close filter sheet"
            (click)="onCloseClick()"
          >
            <span aria-hidden="true">&times;</span>
          </button>
        </div>
        <div class="filter-sheet__body">
          <fd-issue-filter-rail [touch]="true" />
        </div>
      </div>
    }
  `,
  styleUrl: './issue-filter-sheet.scss',
})
export class IssueFilterSheetComponent {
  private readonly _hostRef = inject(ElementRef) as ElementRef<HTMLElement>;

  readonly open = input(false);
  readonly triggerRef = input<ElementRef<HTMLElement> | undefined>(undefined);

  readonly close: OutputEmitterRef<void> = output<void>();

  protected readonly headingId = HEADING_ID;

  private readonly _focusEffect = afterRenderEffect(() => {
    const isOpen = this.open();
    if (isOpen) {
      const panel = this._getPanel();
      if (panel !== null) {
        const closeBtn = panel.querySelector<HTMLElement>('.filter-sheet__close-btn');
        (closeBtn ?? panel).focus();
      }
    } else {
      const trigger = this.triggerRef()?.nativeElement;
      if (trigger !== undefined && trigger !== null) {
        trigger.focus();
      }
    }
  });

  @HostListener('keydown', ['$event'])
  protected onHostKeydown(event: KeyboardEvent): void {
    if (!this.open()) {
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      this.close.emit();
      return;
    }
    if (event.key === 'Tab') {
      this._trapFocus(event);
    }
  }

  protected onScrimClick(): void {
    this.close.emit();
  }

  protected onCloseClick(): void {
    this.close.emit();
  }

  private _getPanel(): HTMLElement | null {
    return this._hostRef.nativeElement.querySelector<HTMLElement>('.filter-sheet__panel');
  }

  private _trapFocus(event: KeyboardEvent): void {
    const panel = this._getPanel();
    if (panel === null) {
      return;
    }

    const focusable = Array.from(
      panel.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)
    );

    if (focusable.length === 0) {
      event.preventDefault();
      return;
    }

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement as HTMLElement;

    if (event.shiftKey) {
      if (active === first || !panel.contains(active)) {
        event.preventDefault();
        last.focus();
      }
    } else {
      if (active === last || !panel.contains(active)) {
        event.preventDefault();
        first.focus();
      }
    }
  }
}
