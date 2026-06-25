import { Component, ElementRef, QueryList, Signal, ViewChildren, inject } from '@angular/core';
import { Toast, ToastService } from '../../../core/services/toast.service';

@Component({
  selector: 'fd-toast',
  standalone: true,
  templateUrl: './toast-host.html',
  styleUrl: './toast-host.scss',
})
export class ToastHostComponent {
  private readonly _toastService = inject(ToastService);
  private readonly _elementRef = inject(ElementRef);

  @ViewChildren('dismissBtn') private readonly _dismissButtons!: QueryList<ElementRef<HTMLButtonElement>>;

  readonly toasts: Signal<Toast[]> = this._toastService.toasts;

  private _priorFocusedElement: HTMLElement | null = null;
  private _pendingKeyboardDismissIndex: number | null = null;

  onHostFocusIn(event: FocusEvent): void {
    const host = this._elementRef.nativeElement as HTMLElement;
    const relatedTarget = event.relatedTarget as HTMLElement | null;
    // Capture prior focus only when arriving from OUTSIDE the toast stack
    if (relatedTarget === null || !host.contains(relatedTarget)) {
      this._priorFocusedElement = relatedTarget;
    }
  }

  dismissFromButton(id: number, toastIndex: number): void {
    this._pendingKeyboardDismissIndex = toastIndex;
    this._toastService.dismiss(id);
    // Move focus after the signal update propagates to DOM (microtask)
    Promise.resolve().then(() => {
      this._moveFocusAfterDismiss();
    });
  }

  dismiss(id: number): void {
    // Mouse-path dismiss: no focus management
    this._toastService.dismiss(id);
  }

  pause(id: number): void {
    this._toastService.pause(id);
  }

  resume(id: number): void {
    this._toastService.resume(id);
  }

  private _moveFocusAfterDismiss(): void {
    const dismissedIndex = this._pendingKeyboardDismissIndex;
    this._pendingKeyboardDismissIndex = null;

    const buttons = this._dismissButtons.toArray();
    if (buttons.length === 0) {
      // No toasts remain — restore prior focus
      if (this._priorFocusedElement !== null) {
        this._priorFocusedElement.focus();
        this._priorFocusedElement = null;
      }
      return;
    }

    if (dismissedIndex === null) {
      return;
    }

    // Prefer the button at the same index (next toast), fall back to previous
    const targetIndex = dismissedIndex < buttons.length ? dismissedIndex : buttons.length - 1;
    buttons[targetIndex].nativeElement.focus();
  }
}
