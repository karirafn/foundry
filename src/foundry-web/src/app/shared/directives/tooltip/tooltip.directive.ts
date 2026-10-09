import {
  Directive,
  ElementRef,
  OnDestroy,
  inject,
  input,
} from '@angular/core';
import { Overlay, OverlayRef } from '@angular/cdk/overlay';

let _tooltipIdCounter = 0;

const TOOLTIP_PANEL_CLASS = 'fd-tooltip-panel';

@Directive({
  selector: '[fdTooltip]',
  standalone: true,
  host: {
    '(mouseenter)': 'onMouseenter($event)',
    '(mouseleave)': 'onMouseleave($event)',
    '(focus)': 'onFocus()',
    '(blur)': 'onBlur()',
    '(pointerdown)': 'onPointerdown($event)',
  },
})
export class TooltipDirective implements OnDestroy {
  readonly fdTooltip = input<string | null>(null);

  private readonly _overlay = inject(Overlay);
  private readonly _el = inject<ElementRef<HTMLElement>>(ElementRef);

  private _overlayRef: OverlayRef | null = null;
  private _panelEl: HTMLElement | null = null;
  private _tooltipId = `fd-tooltip-${++_tooltipIdCounter}`;
  private _escapeListener: ((e: KeyboardEvent) => void) | null = null;

  protected onMouseenter(_event: MouseEvent): void {
    this._show();
  }

  protected onMouseleave(event: MouseEvent): void {
    // Stay open while the pointer moves from the trigger onto the tooltip panel.
    if (this._panelEl && event.relatedTarget instanceof Node && this._panelEl.contains(event.relatedTarget as Node)) {
      return;
    }
    this._hide();
  }

  protected onFocus(): void {
    this._show();
  }

  protected onBlur(): void {
    this._hide();
  }

  protected onPointerdown(event: PointerEvent): void {
    if (event.pointerType !== 'touch') {
      return;
    }
    if (this._panelEl) {
      this._hide();
    } else {
      this._show();
    }
  }

  ngOnDestroy(): void {
    this._destroyOverlay();
  }

  private _show(): void {
    const text = this.fdTooltip();
    if (!text) {
      return;
    }

    if (this._panelEl) {
      return;
    }

    this._createOverlay();

    const panelEl = document.createElement('div');
    panelEl.id = this._tooltipId;
    panelEl.setAttribute('role', 'tooltip');
    panelEl.textContent = text;
    panelEl.className = TOOLTIP_PANEL_CLASS;
    this._stylePanel(panelEl);

    // Stay open while the pointer moves from the trigger onto the panel (WCAG 1.4.13).
    panelEl.addEventListener('mouseleave', (e: MouseEvent) => {
      if (
        e.relatedTarget instanceof Node &&
        this._el.nativeElement.contains(e.relatedTarget as Node)
      ) {
        return;
      }
      this._hide();
    });

    this._panelEl = panelEl;
    this._overlayRef!.overlayElement.appendChild(panelEl);

    this._el.nativeElement.setAttribute('aria-describedby', this._tooltipId);

    this._escapeListener = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        this._hide();
      }
    };
    document.addEventListener('keydown', this._escapeListener);
  }

  private _hide(): void {
    this._el.nativeElement.removeAttribute('aria-describedby');

    if (this._escapeListener) {
      document.removeEventListener('keydown', this._escapeListener);
      this._escapeListener = null;
    }

    if (this._panelEl) {
      this._panelEl.remove();
      this._panelEl = null;
    }
  }

  private _createOverlay(): void {
    if (this._overlayRef) {
      return;
    }

    const positionStrategy = this._overlay
      .position()
      .flexibleConnectedTo(this._el.nativeElement)
      .withPositions([
        // Prefer above.
        {
          originX: 'center',
          originY: 'top',
          overlayX: 'center',
          overlayY: 'bottom',
          offsetY: -8,
        },
        // Flip below.
        {
          originX: 'center',
          originY: 'bottom',
          overlayX: 'center',
          overlayY: 'top',
          offsetY: 8,
        },
      ]);

    this._overlayRef = this._overlay.create({
      positionStrategy,
      scrollStrategy: this._overlay.scrollStrategies.reposition(),
      maxWidth: '16rem',
    });
  }

  private _destroyOverlay(): void {
    this._hide();
    if (this._overlayRef) {
      this._overlayRef.dispose();
      this._overlayRef = null;
    }
  }

  private _stylePanel(el: HTMLElement): void {
    el.style.cssText = [
      'background: var(--fd-bg-card)',
      'border: 1px solid var(--fd-border-default)',
      'border-radius: var(--fd-radius-sm)',
      'color: var(--fd-text-primary)',
      'font-size: 0.75rem',
      'padding: var(--fd-spacing-xs) var(--fd-spacing-sm)',
      'max-width: 16rem',
      'box-shadow: 0 2px 8px rgb(0 0 0 / 0.3)',
      'word-wrap: break-word',
    ].join('; ');
  }
}
