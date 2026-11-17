import { DestroyRef, Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Observable, merge } from 'rxjs';
import { catchError, debounceTime, filter, switchMap, tap } from 'rxjs/operators';
import { extractErrorMessage } from '../../../core/http/extract-error-message';
import { LoadOutcome } from '../../../core/models/load-outcome';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import {
  CredentialFieldErrors,
  PackageSource,
  PendingCheck,
  PendingCheckAction,
  SaveCredentialRequest,
  resolvedAnnouncement,
} from './package-source.model';

const API_BASE = '/api/package-sources';
const RELOAD_DEBOUNCE_MS = 300;
/** How long a pending check waits for the server's verdict before its automatic exit. */
export const PENDING_CHECK_TIMEOUT_MS = 60_000;
const SAVE_ERROR = 'Failed to save credential';
const REMOVE_ERROR = 'Failed to remove credential';
const LOAD_ERROR = 'Failed to load package sources';

interface ValidationProblem {
  errors?: Record<string, string[] | undefined>;
}

@Injectable({ providedIn: 'root' })
export class PackageSourceService {
  private readonly _http = inject(HttpClient);
  private readonly _signalR = inject(SystemSignalRService);

  private readonly _sources: WritableSignal<PackageSource[]> = signal([]);
  readonly sources: Signal<PackageSource[]> = this._sources.asReadonly();

  private readonly _loading: WritableSignal<boolean> = signal(false);
  readonly loading: Signal<boolean> = this._loading.asReadonly();

  private readonly _loadError: WritableSignal<string | null> = signal(null);
  readonly loadError: Signal<string | null> = this._loadError.asReadonly();

  private readonly _saving: WritableSignal<boolean> = signal(false);
  readonly saving: Signal<boolean> = this._saving.asReadonly();

  private readonly _saveSuccess: WritableSignal<boolean> = signal(false);
  readonly saveSuccess: Signal<boolean> = this._saveSuccess.asReadonly();

  private readonly _saveError: WritableSignal<string | null> = signal(null);
  readonly saveError: Signal<string | null> = this._saveError.asReadonly();

  private readonly _fieldErrors: WritableSignal<CredentialFieldErrors> = signal({});
  readonly fieldErrors: Signal<CredentialFieldErrors> = this._fieldErrors.asReadonly();

  private readonly _removingId: WritableSignal<string | null> = signal(null);
  readonly removingId: Signal<string | null> = this._removingId.asReadonly();

  private readonly _removeError: WritableSignal<string | null> = signal(null);
  readonly removeError: Signal<string | null> = this._removeError.asReadonly();

  private readonly _pendingChecks: WritableSignal<ReadonlyMap<string, PendingCheck>> = signal(new Map());
  readonly pendingChecks: Signal<ReadonlyMap<string, PendingCheck>> = this._pendingChecks.asReadonly();

  private readonly _announcement: WritableSignal<string> = signal('');
  readonly announcement: Signal<string> = this._announcement.asReadonly();

  private readonly _pendingTimers = new Map<string, ReturnType<typeof setTimeout>>();
  private _started = false;

  constructor() {
    merge(this._signalR.reconnected, this._signalR.reloadTrigger)
      .pipe(
        filter(() => this._started),
        debounceTime(RELOAD_DEBOUNCE_MS),
        switchMap(() => this._fetch().pipe(catchError(() => EMPTY))),
        takeUntilDestroyed(),
      )
      .subscribe();

    inject(DestroyRef).onDestroy(() => {
      for (const timer of this._pendingTimers.values()) {
        clearTimeout(timer);
      }
      this._pendingTimers.clear();
    });
  }

  load(): Promise<LoadOutcome> {
    this._started = true;
    this._loadError.set(null);
    this._loading.set(true);

    return new Promise<LoadOutcome>((resolve) => {
      this._fetch().subscribe({
        next: () => {
          this._loading.set(false);
          resolve('loaded');
        },
        error: (err: HttpErrorResponse) => {
          console.error(err);
          this._loadError.set(LOAD_ERROR);
          this._loading.set(false);
          resolve('failed');
        },
      });
    });
  }

  saveCredential(id: string, request: SaveCredentialRequest): void {
    this.clearSaveState();
    this._saving.set(true);

    this._http.put<void>(`${API_BASE}/${id}/credential`, request).subscribe({
      next: () => {
        const source = this._find(id);
        this._sources.update((sources) =>
          sources.map((s) =>
            s.id === id ? { ...s, credential: { username: request.username, updatedAt: new Date().toISOString() } } : s,
          ),
        );
        if (source) {
          this._startPendingCheck(source, 'saved');
          this._announcement.set(`Credential saved for ${source.url}. Checking access…`);
        }
        this._saving.set(false);
        this._saveSuccess.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        const fieldErrors = err.status === 400 ? this._fieldErrorsOf(err) : {};
        if (fieldErrors.username !== undefined || fieldErrors.token !== undefined) {
          this._fieldErrors.set(fieldErrors);
        } else {
          this._saveError.set(extractErrorMessage(err) ?? SAVE_ERROR);
        }
        this._saving.set(false);
      },
    });
  }

  /** Resolves true once the credential is removed. */
  removeCredential(id: string): Promise<boolean> {
    this._removeError.set(null);
    this._removingId.set(id);

    return new Promise<boolean>((resolve) => {
      this._http.delete<void>(`${API_BASE}/${id}/credential`).subscribe({
        next: () => {
          const source = this._find(id);
          this._sources.update((sources) => sources.map((s) => (s.id === id ? { ...s, credential: null } : s)));
          if (source) {
            this._startPendingCheck(source, 'removed');
            this._announcement.set(`Credential removed for ${source.url}. Checking access…`);
          }
          this._removingId.set(null);
          resolve(true);
        },
        error: (err: HttpErrorResponse) => {
          console.error(err);
          this._removeError.set(REMOVE_ERROR);
          this._removingId.set(null);
          resolve(false);
        },
      });
    });
  }

  clearSaveState(): void {
    this._saveSuccess.set(false);
    this._saveError.set(null);
    this._fieldErrors.set({});
  }

  /** Drops a field's server error once the operator edits that field. */
  clearFieldError(field: keyof CredentialFieldErrors): void {
    this._fieldErrors.update((errors) => {
      if (errors[field] === undefined) {
        return errors;
      }
      const next = { ...errors };
      delete next[field];
      return next;
    });
  }

  private _fetch(): Observable<PackageSource[]> {
    return this._http.get<PackageSource[]>(API_BASE).pipe(
      tap((sources) => {
        this._sources.set(sources);
        this._resolvePendingChecks(sources);
      }),
    );
  }

  private _startPendingCheck(source: PackageSource, action: PendingCheckAction): void {
    this._pendingChecks.update((checks) => new Map(checks).set(source.id, { baseline: source.lastCheckedAt, action }));
    this._clearTimer(source.id);
    this._pendingTimers.set(
      source.id,
      setTimeout(() => this._exitPendingCheck(source.id, source.url), PENDING_CHECK_TIMEOUT_MS),
    );
  }

  /** The automatic exit: one more fetch, then the pending entry goes whatever it shows. */
  private _exitPendingCheck(id: string, url: string): void {
    this._pendingTimers.delete(id);
    const giveUp = (): void => {
      if (!this._pendingChecks().has(id)) {
        return;
      }
      this._removePending(id);
      this._announcement.set(
        `The access check for ${url} is taking longer than expected. The status updates when it finishes.`,
      );
    };
    this._fetch().subscribe({ next: giveUp, error: giveUp });
  }

  private _resolvePendingChecks(sources: PackageSource[]): void {
    const checks = this._pendingChecks();
    if (checks.size === 0) {
      return;
    }
    const announcements: string[] = [];
    for (const [id, check] of checks) {
      const source = sources.find((s) => s.id === id);
      if (!source) {
        this._removePending(id);
        continue;
      }
      if (source.lastCheckedAt !== check.baseline) {
        this._removePending(id);
        announcements.push(resolvedAnnouncement(source));
      }
    }
    if (announcements.length > 0) {
      this._announcement.set(announcements.join(' '));
    }
  }

  private _removePending(id: string): void {
    this._clearTimer(id);
    this._pendingChecks.update((checks) => {
      const next = new Map(checks);
      next.delete(id);
      return next;
    });
  }

  private _clearTimer(id: string): void {
    const timer = this._pendingTimers.get(id);
    if (timer !== undefined) {
      clearTimeout(timer);
      this._pendingTimers.delete(id);
    }
  }

  private _find(id: string): PackageSource | undefined {
    return this._sources().find((s) => s.id === id);
  }

  private _fieldErrorsOf(err: HttpErrorResponse): CredentialFieldErrors {
    const body = err.error as ValidationProblem | null;
    const errors = body !== null && typeof body === 'object' ? body.errors : undefined;
    if (!errors) {
      return {};
    }
    const result: CredentialFieldErrors = {};
    // ASP.NET may key the errors in either case; the contract names them username / token.
    for (const [key, messages] of Object.entries(errors)) {
      const field = key.toLowerCase();
      const message = messages?.[0];
      if (message !== undefined && (field === 'username' || field === 'token')) {
        result[field] = message;
      }
    }
    return result;
  }
}
