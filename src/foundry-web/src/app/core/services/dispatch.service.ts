import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { GlobalSettingsResponse } from '../../features/settings/settings.model';

const PAUSE_DISPATCH_ERROR = 'Failed to pause dispatch';
const RESUME_DISPATCH_ERROR = 'Failed to resume dispatch';

@Injectable({ providedIn: 'root' })
export class DispatchService {
  private readonly _http = inject(HttpClient);

  private readonly _isDispatchPausedSignal: WritableSignal<boolean> = signal(false);
  readonly isDispatchPaused: Signal<boolean> = this._isDispatchPausedSignal.asReadonly();

  private readonly _usageLimitResetsAtSignal: WritableSignal<string | null> = signal(null);
  readonly usageLimitResetsAt: Signal<string | null> = this._usageLimitResetsAtSignal.asReadonly();

  private readonly _pausingSignal: WritableSignal<boolean> = signal(false);
  readonly pausing: Signal<boolean> = this._pausingSignal.asReadonly();

  private readonly _resumingSignal: WritableSignal<boolean> = signal(false);
  readonly resuming: Signal<boolean> = this._resumingSignal.asReadonly();

  private readonly _pauseResumeErrorSignal: WritableSignal<string | null> = signal(null);
  readonly pauseResumeError: Signal<string | null> = this._pauseResumeErrorSignal.asReadonly();

  updateFromSettings(response: GlobalSettingsResponse): void {
    this._isDispatchPausedSignal.set(response.isDispatchPaused);
    this._usageLimitResetsAtSignal.set(response.usageLimitResetsAt);
  }

  pauseDispatch(): void {
    this._pauseResumeErrorSignal.set(null);
    this._pausingSignal.set(true);

    this._http.post<GlobalSettingsResponse>('/api/settings/dispatch/pause', null).subscribe({
      next: (response) => {
        this.updateFromSettings(response);
        this._pausingSignal.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._pauseResumeErrorSignal.set(PAUSE_DISPATCH_ERROR);
        this._pausingSignal.set(false);
      },
    });
  }

  resumeDispatch(): void {
    this._pauseResumeErrorSignal.set(null);
    this._resumingSignal.set(true);

    this._http.post<GlobalSettingsResponse>('/api/settings/dispatch/resume', null).subscribe({
      next: (response) => {
        this.updateFromSettings(response);
        this._resumingSignal.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._pauseResumeErrorSignal.set(RESUME_DISPATCH_ERROR);
        this._resumingSignal.set(false);
      },
    });
  }
}
