import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { components } from '../../api/schema';

type GeneratedDispatchCapacity = components['schemas']['DispatchCapacity'];

/** Dispatch-slot usage as counted by the dispatch gate (reservations plus starting and active runs). */
export interface DispatchCapacity {
  readonly maxConcurrent: number;
  readonly occupiedSlots: number;
  readonly freeSlots: number;
}

const LOAD_CAPACITY_ERROR = 'Failed to load dispatch capacity';

@Injectable({ providedIn: 'root' })
export class DispatchCapacityService {
  private readonly _http = inject(HttpClient);

  private readonly _capacitySignal: WritableSignal<DispatchCapacity | null> = signal(null);
  readonly capacity: Signal<DispatchCapacity | null> = this._capacitySignal.asReadonly();

  private readonly _loadErrorSignal: WritableSignal<string | null> = signal(null);
  readonly loadError: Signal<string | null> = this._loadErrorSignal.asReadonly();

  private _requestToken = 0;

  load(): void {
    this._requestToken += 1;
    const token = this._requestToken;

    this._http.get<GeneratedDispatchCapacity>('/api/workers/capacity').subscribe({
      next: (response) => {
        if (token !== this._requestToken) {
          return;
        }
        this._capacitySignal.set({
          maxConcurrent: toCount(response.maxConcurrent),
          occupiedSlots: toCount(response.occupiedSlots),
          freeSlots: toCount(response.freeSlots),
        });
        this._loadErrorSignal.set(null);
      },
      error: (err: HttpErrorResponse) => {
        if (token !== this._requestToken) {
          return;
        }
        console.error(err);
        this._loadErrorSignal.set(LOAD_CAPACITY_ERROR);
      },
    });
  }
}

// The generated schema widens int32 to number | string; non-finite or negative values degrade to 0.
function toCount(value: number | string): number {
  const n = Number(value);
  return Number.isFinite(n) && n > 0 ? Math.floor(n) : 0;
}
