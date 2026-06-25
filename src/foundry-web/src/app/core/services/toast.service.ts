import { Injectable, OnDestroy, Signal, WritableSignal, signal } from '@angular/core';

export interface Toast {
  id: number;
  message: string;
}

const AUTO_DISMISS_MS = 5000;
const MAX_MESSAGE_LENGTH = 200;
const MAX_QUEUE_DEPTH = 5;

interface ToastTimer {
  handle: ReturnType<typeof setTimeout> | null;
  remainingMs: number;
  shownAt: number;
}

@Injectable({ providedIn: 'root' })
export class ToastService implements OnDestroy {
  private _nextId = 0;
  private readonly _timers: Map<number, ToastTimer> = new Map();

  private readonly _toastsSignal: WritableSignal<Toast[]> = signal([]);
  readonly toasts: Signal<Toast[]> = this._toastsSignal.asReadonly();

  show(message: string): void {
    const sanitized = message.trim().slice(0, MAX_MESSAGE_LENGTH);
    const id = ++this._nextId;
    const toast: Toast = { id, message: sanitized };

    const current = this._toastsSignal();
    if (current.length >= MAX_QUEUE_DEPTH) {
      const oldest = current[0];
      this._cancelTimer(oldest.id);
      this._toastsSignal.update((q) => q.slice(1));
    }

    this._toastsSignal.update((q) => [...q, toast]);
    this._scheduleTimer(id, AUTO_DISMISS_MS);
  }

  dismiss(id: number): void {
    this._cancelTimer(id);
    this._removeToast(id);
  }

  pause(id: number): void {
    const entry = this._timers.get(id);
    if (!entry || entry.handle === null) {
      return;
    }
    const elapsed = Date.now() - entry.shownAt;
    const remaining = Math.max(0, entry.remainingMs - elapsed);
    clearTimeout(entry.handle);
    this._timers.set(id, { handle: null, remainingMs: remaining, shownAt: entry.shownAt });
  }

  resume(id: number): void {
    const entry = this._timers.get(id);
    if (!entry || entry.handle !== null) {
      return;
    }
    this._scheduleTimer(id, entry.remainingMs);
  }

  ngOnDestroy(): void {
    for (const entry of this._timers.values()) {
      if (entry.handle !== null) {
        clearTimeout(entry.handle);
      }
    }
    this._timers.clear();
    this._toastsSignal.set([]);
  }

  private _scheduleTimer(id: number, durationMs: number): void {
    const shownAt = Date.now();
    const handle = setTimeout(() => {
      this._removeToast(id);
      this._timers.delete(id);
    }, durationMs);
    this._timers.set(id, { handle, remainingMs: durationMs, shownAt });
  }

  private _cancelTimer(id: number): void {
    const entry = this._timers.get(id);
    if (entry && entry.handle !== null) {
      clearTimeout(entry.handle);
    }
    this._timers.delete(id);
  }

  private _removeToast(id: number): void {
    this._toastsSignal.update((current) => current.filter((t) => t.id !== id));
  }
}
