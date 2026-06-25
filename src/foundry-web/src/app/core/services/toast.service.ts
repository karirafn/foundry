import { Injectable, OnDestroy, Signal, WritableSignal, signal } from '@angular/core';

export interface Toast {
  id: number;
  message: string;
}

const AUTO_DISMISS_MS = 5000;

@Injectable({ providedIn: 'root' })
export class ToastService implements OnDestroy {
  private _nextId = 0;
  private readonly _timers: Map<number, ReturnType<typeof setTimeout>> = new Map();

  private readonly _toastsSignal: WritableSignal<Toast[]> = signal([]);
  readonly toasts: Signal<Toast[]> = this._toastsSignal.asReadonly();

  show(message: string): void {
    const id = ++this._nextId;
    const toast: Toast = { id, message };
    this._toastsSignal.update((current) => [...current, toast]);

    const handle = setTimeout(() => {
      this._removeToast(id);
      this._timers.delete(id);
    }, AUTO_DISMISS_MS);

    this._timers.set(id, handle);
  }

  dismiss(id: number): void {
    const handle = this._timers.get(id);
    if (handle !== undefined) {
      clearTimeout(handle);
      this._timers.delete(id);
    }
    this._removeToast(id);
  }

  ngOnDestroy(): void {
    for (const handle of this._timers.values()) {
      clearTimeout(handle);
    }
    this._timers.clear();
    this._toastsSignal.set([]);
  }

  private _removeToast(id: number): void {
    this._toastsSignal.update((current) => current.filter((t) => t.id !== id));
  }
}
