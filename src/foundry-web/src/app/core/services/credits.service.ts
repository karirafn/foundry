import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ClaudeAccountSummary } from '../models/settings.model';
import { SystemSignalRService } from './system-signalr.service';

interface CheckCreditsNowResponse {
  inFlight: boolean;
  outcome: string | null;
}

@Injectable({ providedIn: 'root' })
export class CreditsService {
  private readonly _http = inject(HttpClient);
  private readonly _signalR = inject(SystemSignalRService);

  private readonly _nextProbeAtSignal: WritableSignal<string | null> = signal(null);
  readonly nextProbeAt: Signal<string | null> = this._nextProbeAtSignal.asReadonly();

  private readonly _isCheckingSignal: WritableSignal<boolean> = signal(false);
  readonly isChecking: Signal<boolean> = this._isCheckingSignal.asReadonly();

  constructor() {
    this._signalR.creditsNotification
      .pipe(takeUntilDestroyed())
      .subscribe((notification) => {
        if (notification.isActive) {
          this._fetchCredentials();
        } else {
          this._nextProbeAtSignal.set(null);
          this._isCheckingSignal.set(false);
        }
      });
  }

  updateFromCredentials(summary: ClaudeAccountSummary): void {
    this._nextProbeAtSignal.set(summary.nextProbeAt);
  }

  checkNow(): void {
    if (this._isCheckingSignal()) {
      return;
    }
    this._isCheckingSignal.set(true);

    this._http.post<CheckCreditsNowResponse>('/api/credentials/probe', null).subscribe({
      next: (response) => {
        if (!response.inFlight) {
          this._isCheckingSignal.set(false);
        }
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._isCheckingSignal.set(false);
      },
    });
  }

  private _fetchCredentials(): void {
    this._http.get<ClaudeAccountSummary>('/api/credentials').subscribe({
      next: (summary) => {
        this.updateFromCredentials(summary);
        this._isCheckingSignal.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._isCheckingSignal.set(false);
      },
    });
  }
}
