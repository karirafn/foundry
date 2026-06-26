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
const SWIPE_DISMISS_THRESHOLD_PX = 96;
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
        (pointerdown)="onPanelPointerDown($event)"
        (pointermove)="onPanelPointerMove($event)"
        (pointerup)="onPanelPointerUp($event)"
        (pointercancel)="onPanelPointerCancel()"
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
          <fd-issue-filter-rail [touch]="true" idPrefix="sheet-" />
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

  private _dragStartY: number | null = null;

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

  /** Returns true when the drag distance exceeds the dismiss threshold. */
  shouldDismiss(dragDistancePx: number): boolean {
    return dragDistancePx >= SWIPE_DISMISS_THRESHOLD_PX;
  }

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

  protected onPanelPointerDown(event: PointerEvent): void {
    this._dragStartY = event.clientY;
    const panel = this._getPanel();
    if (panel !== null && typeof panel.setPointerCapture === 'function') {
      panel.setPointerCapture(event.pointerId);
    }
  }

  protected onPanelPointerMove(event: PointerEvent): void {
    if (this._dragStartY === null) {
      return;
    }
    const delta = event.clientY - this._dragStartY;
    if (delta <= 0) {
      return;
    }
    const panel = this._getPanel();
    if (panel !== null) {
      panel.style.transform = `translateY(${delta}px)`;
      panel.style.transition = 'none';
    }
  }

  protected onPanelPointerUp(event: PointerEvent): void {
    if (this._dragStartY === null) {
      return;
    }
    const delta = Math.max(0, event.clientY - this._dragStartY);
    this._dragStartY = null;
    if (this.shouldDismiss(delta)) {
      this.close.emit();
    } else {
      this._snapBack();
    }
  }

  protected onPanelPointerCancel(): void {
    this._dragStartY = null;
    this._snapBack();
  }

  private _snapBack(): void {
    const panel = this._getPanel();
    if (panel === null) {
      return;
    }
    const prefersReducedMotion =
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    panel.style.transition = prefersReducedMotion ? 'none' : 'transform 200ms ease';
    panel.style.transform = 'translateY(0)';
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
