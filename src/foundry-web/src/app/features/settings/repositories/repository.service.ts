import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { EMPTY, Observable, Subject, forkJoin, merge, of } from 'rxjs';
import { catchError, debounceTime, filter, groupBy, map, mergeMap, switchMap, tap } from 'rxjs/operators';
import { extractErrorMessage } from '../../../core/http/extract-error-message';
import { LoadOutcome } from '../../../core/models/load-outcome';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { DeleteFailure, deleteFailureFrom } from '../delete-failure/delete-failure.model';
import { ApplyBranchRulesResponse, AvailableRepositoriesResponse, AvailableRepository, CreateRepositoryRequest, CreateRepositoryResponse, RegisterWebhookResponse, RepositorySummary, RepositoryWebhookInfo, RepositoryWebhookSecret, UpdateRepositoryRequest, WebhookStatus } from './repository.model';

export const LIVE_UPDATE_DEBOUNCE_MS = 300;

/** A live webhook update: one repository's re-fetched info, or every repository may have changed. */
export type RepositoryWebhookUpdate =
  | { kind: 'repository'; repositoryId: string; info: RepositoryWebhookInfo }
  | { kind: 'all' };

@Injectable({ providedIn: 'root' })
export class RepositoryService {
  private readonly _http = inject(HttpClient);
  private readonly _signalR = inject(SystemSignalRService);

  private readonly _webhookUpdates = new Subject<RepositoryWebhookUpdate>();
  readonly webhookUpdates: Observable<RepositoryWebhookUpdate> = this._webhookUpdates.asObservable();

  /** The accounts of the last foreground load; null until a list has been loaded once. */
  private _loadedAccountIds: string[] | null = null;

  /** Moves on every list change the operator makes, so a background reload sent before it is dropped. */
  private _listVersion = 0;

  private readonly _repositoriesSignal: WritableSignal<RepositorySummary[]> = signal([]);
  readonly repositories: Signal<RepositorySummary[]> = this._repositoriesSignal.asReadonly();

  private readonly _loadingSignal: WritableSignal<boolean> = signal(false);
  readonly loading: Signal<boolean> = this._loadingSignal.asReadonly();

  private readonly _loadErrorSignal: WritableSignal<string | null> = signal(null);
  readonly loadError: Signal<string | null> = this._loadErrorSignal.asReadonly();

  private readonly _savingSignal: WritableSignal<boolean> = signal(false);
  readonly saving: Signal<boolean> = this._savingSignal.asReadonly();

  private readonly _saveSuccessSignal: WritableSignal<boolean> = signal(false);
  readonly saveSuccess: Signal<boolean> = this._saveSuccessSignal.asReadonly();

  private readonly _saveErrorSignal: WritableSignal<string | null> = signal(null);
  readonly saveError: Signal<string | null> = this._saveErrorSignal.asReadonly();

  private readonly _deletingSignal: WritableSignal<boolean> = signal(false);
  readonly deleting: Signal<boolean> = this._deletingSignal.asReadonly();

  private readonly _deleteFailureSignal: WritableSignal<DeleteFailure | null> = signal(null);
  readonly deleteFailure: Signal<DeleteFailure | null> = this._deleteFailureSignal.asReadonly();

  private readonly _availableRepositoriesSignal: WritableSignal<AvailableRepository[]> = signal([]);
  readonly availableRepositories: Signal<AvailableRepository[]> = this._availableRepositoriesSignal.asReadonly();

  private readonly _availableHasClaimsSignal: WritableSignal<boolean> = signal(false);
  readonly availableHasClaims: Signal<boolean> = this._availableHasClaimsSignal.asReadonly();

  private readonly _availableNoPushAccessExplanationSignal: WritableSignal<string> = signal('');
  readonly availableNoPushAccessExplanation: Signal<string> = this._availableNoPushAccessExplanationSignal.asReadonly();

  private readonly _loadingAvailableSignal: WritableSignal<boolean> = signal(false);
  readonly loadingAvailable: Signal<boolean> = this._loadingAvailableSignal.asReadonly();

  private readonly _loadAvailableErrorSignal: WritableSignal<string | null> = signal(null);
  readonly loadAvailableError: Signal<string | null> = this._loadAvailableErrorSignal.asReadonly();

  constructor() {
    this._signalR.repositoryWebhookChanged
      .pipe(
        filter(() => this._loadedAccountIds !== null),
        groupBy((change) => change.repositoryId),
        mergeMap((changes) =>
          changes.pipe(
            debounceTime(LIVE_UPDATE_DEBOUNCE_MS),
            switchMap(({ repositoryId }) => this._refreshWebhook(repositoryId)),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe();

    merge(this._signalR.repositoryWebhooksChanged, this._signalR.reconnected)
      .pipe(
        filter(() => this._loadedAccountIds !== null),
        debounceTime(LIVE_UPDATE_DEBOUNCE_MS),
        tap(() => this._webhookUpdates.next({ kind: 'all' })),
        switchMap(() => this._refreshRepositories()),
        takeUntilDestroyed(),
      )
      .subscribe();
  }

  loadRepositories(accountId: string): void {
    this._loadedAccountIds = [accountId];
    this._listVersion++;
    this._loadErrorSignal.set(null);
    this._loadingSignal.set(true);

    this._http.get<RepositorySummary[]>(this._repositoriesUrl(accountId)).subscribe({
      next: (repositories) => {
        this._repositoriesSignal.set(repositories);
        this._loadingSignal.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._loadErrorSignal.set(extractErrorMessage(err) ?? err.message);
        this._loadingSignal.set(false);
      },
    });
  }

  loadAllRepositories(accountIds: string[]): Promise<LoadOutcome> {
    this._loadedAccountIds = [...accountIds];
    this._listVersion++;
    this._loadErrorSignal.set(null);
    this._loadingSignal.set(true);

    if (accountIds.length === 0) {
      this._repositoriesSignal.set([]);
      this._loadingSignal.set(false);
      return Promise.resolve('loaded');
    }

    const requests = accountIds.map(accountId =>
      this._http.get<RepositorySummary[]>(this._repositoriesUrl(accountId))
    );

    return new Promise<LoadOutcome>((resolve) => {
      forkJoin(requests).subscribe({
        next: (results) => {
          const sorted = results.flat().sort((a, b) => a.position - b.position);
          this._repositoriesSignal.set(sorted);
          this._loadingSignal.set(false);
          resolve('loaded');
        },
        error: (err: HttpErrorResponse) => {
          console.error(err);
          this._loadErrorSignal.set(extractErrorMessage(err) ?? err.message);
          this._loadingSignal.set(false);
          resolve('failed');
        },
      });
    });
  }

  loadAvailableRepositories(accountId: string): void {
    this._loadAvailableErrorSignal.set(null);
    this._loadingAvailableSignal.set(true);
    this._availableHasClaimsSignal.set(false);

    this._http.get<AvailableRepositoriesResponse>(`${this._repositoriesUrl(accountId)}/available-repositories`).subscribe({
      next: (response) => {
        this._availableRepositoriesSignal.set(response.repositories);
        this._availableHasClaimsSignal.set(response.hasClaims);
        this._availableNoPushAccessExplanationSignal.set(response.noPushAccessExplanation ?? '');
        this._loadingAvailableSignal.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._availableHasClaimsSignal.set(false);
        this._loadAvailableErrorSignal.set(extractErrorMessage(err) ?? err.message);
        this._loadingAvailableSignal.set(false);
      },
    });
  }

  createRepository(accountId: string, request: CreateRepositoryRequest): Observable<CreateRepositoryResponse> {
    this._saveErrorSignal.set(null);
    this._saveSuccessSignal.set(false);
    this._savingSignal.set(true);

    return this._http.post<CreateRepositoryResponse>(this._repositoriesUrl(accountId), request).pipe(
      tap((response) => {
        this._listVersion++;
        this._repositoriesSignal.update(repositories => [...repositories, response.repository]);
        this._savingSignal.set(false);
        this._saveSuccessSignal.set(true);
      }),
      catchError((err: HttpErrorResponse) => {
        console.error(err);
        this._saveErrorSignal.set(extractErrorMessage(err) ?? err.message);
        this._savingSignal.set(false);
        this._saveSuccessSignal.set(false);
        throw err;
      }),
    );
  }

  updateRepository(accountId: string, id: string, request: UpdateRepositoryRequest): Observable<RepositorySummary> {
    this._saveErrorSignal.set(null);
    this._saveSuccessSignal.set(false);
    this._savingSignal.set(true);

    return this._http.put<RepositorySummary>(`${this._repositoriesUrl(accountId)}/${id}`, request).pipe(
      tap((updated) => {
        this._listVersion++;
        this._repositoriesSignal.update(repositories =>
          repositories.map(r => r.id === updated.id ? updated : r)
        );
        this._savingSignal.set(false);
        this._saveSuccessSignal.set(true);
      }),
      catchError((err: HttpErrorResponse) => {
        console.error(err);
        this._saveErrorSignal.set(extractErrorMessage(err) ?? err.message);
        this._savingSignal.set(false);
        this._saveSuccessSignal.set(false);
        throw err;
      }),
    );
  }

  moveRepository(id: string, position: number): Observable<void> {
    this._listVersion++;
    return this._http.patch<void>(`/api/repositories/${id}/position`, { position }).pipe(
      tap(() => {
        this._listVersion++;
        this._repositoriesSignal.update(repositories => {
          const currentIndex = repositories.findIndex(r => r.id === id);
          if (currentIndex === -1) {
            return repositories;
          }
          const updated = [...repositories];
          const [moved] = updated.splice(currentIndex, 1);
          updated.splice(position, 0, moved);
          return updated;
        });
      }),
      catchError((err: HttpErrorResponse) => {
        console.error(err);
        throw err;
      }),
    );
  }

  recheckEligibility(accountId: string, id: string): Observable<RepositorySummary> {
    return this._http.post<RepositorySummary>(`${this._repositoriesUrl(accountId)}/${id}/recheck`, {}).pipe(
      tap((updated) => {
        this._listVersion++;
        this._repositoriesSignal.update(repositories =>
          repositories.map(r => r.id === updated.id ? updated : r)
        );
      }),
      catchError((err: HttpErrorResponse) => {
        console.error(err);
        throw err;
      }),
    );
  }

  applyBranchRules(accountId: string, id: string): Observable<ApplyBranchRulesResponse> {
    return this._http.post<ApplyBranchRulesResponse>(`${this._repositoriesUrl(accountId)}/${id}/apply-branch-rules`, {}).pipe(
      tap((response) => {
        this._listVersion++;
        this._repositoriesSignal.update(repositories =>
          repositories.map(r => r.id === response.repository.id ? response.repository : r)
        );
      }),
      catchError((err: HttpErrorResponse) => {
        console.error(err);
        throw err;
      }),
    );
  }

  getWebhook(accountId: string, id: string): Observable<RepositoryWebhookInfo> {
    return this._http.get<RepositoryWebhookInfo>(`${this._repositoriesUrl(accountId)}/${id}/webhook`);
  }

  /** Reveals the webhook secret on explicit operator request; never cached in a signal. */
  revealWebhookSecret(accountId: string, id: string): Observable<RepositoryWebhookSecret> {
    return this._http.post<RepositoryWebhookSecret>(`${this._repositoriesUrl(accountId)}/${id}/webhook/secret`, {});
  }

  /** Records the operator's "I've added the webhook" action; the hook then waits for its first delivery. */
  markWebhookRegistered(accountId: string, id: string): Observable<RepositoryWebhookInfo> {
    return this._http.post<RepositoryWebhookInfo>(`${this._repositoriesUrl(accountId)}/${id}/webhook/registered`, {}).pipe(
      tap((info) => {
        this._listVersion++;
        this._setWebhookStatus(id, info.status);
      }),
    );
  }

  /** Creates or updates the webhook on the provider with the account token. */
  registerWebhook(accountId: string, id: string): Observable<RegisterWebhookResponse> {
    return this._http.post<RegisterWebhookResponse>(`${this._repositoriesUrl(accountId)}/${id}/webhook/register`, {}).pipe(
      tap((response) => {
        this._listVersion++;
        this._setWebhookStatus(id, response.webhook.status);
      }),
    );
  }

  deleteRepository(accountId: string, id: string): Observable<void> {
    this._deleteFailureSignal.set(null);
    this._deletingSignal.set(true);

    return this._http.delete<void>(`${this._repositoriesUrl(accountId)}/${id}`).pipe(
      tap(() => {
        this._listVersion++;
        this._repositoriesSignal.update(repositories => repositories.filter(r => r.id !== id));
        this._deletingSignal.set(false);
      }),
      catchError((err: HttpErrorResponse) => {
        console.error(err);
        this._deleteFailureSignal.set(deleteFailureFrom(err));
        this._deletingSignal.set(false);
        throw err;
      }),
    );
  }

  /** Re-reads one cached repository's webhook and updates only its row status; never touches the loading state. */
  private _refreshWebhook(repositoryId: string): Observable<unknown> {
    const repository = this._repositoriesSignal().find(r => r.id === repositoryId);
    if (!repository) {
      return EMPTY;
    }

    return this.getWebhook(repository.accountId, repositoryId).pipe(
      tap((info) => {
        this._setWebhookStatus(repositoryId, info.status);
        this._webhookUpdates.next({ kind: 'repository', repositoryId, info });
      }),
      catchError((err: unknown) => {
        console.error(err);
        return EMPTY;
      }),
    );
  }

  /**
   * Reloads the list for the last-loaded accounts and writes only the repositories. The loading and
   * error signals stay untouched, so the list keeps its rows, reorder mode and any open edit form.
   */
  private _refreshRepositories(): Observable<unknown> {
    const accountIds = this._loadedAccountIds ?? [];
    const version = this._listVersion;
    const request: Observable<RepositorySummary[][]> = accountIds.length === 0
      ? of([])
      : forkJoin(accountIds.map(accountId => this._http.get<RepositorySummary[]>(this._repositoriesUrl(accountId))));

    return request.pipe(
      map(results => results.flat().sort((a, b) => a.position - b.position)),
      tap((repositories) => {
        if (version === this._listVersion) {
          this._repositoriesSignal.set(repositories);
        }
      }),
      catchError((err: unknown) => {
        console.error(err);
        return EMPTY;
      }),
    );
  }

  private _setWebhookStatus(id: string, status: WebhookStatus): void {
    this._repositoriesSignal.update(repositories =>
      repositories.map(r => r.id === id ? { ...r, webhookStatus: status } : r)
    );
  }

  private _repositoriesUrl(accountId: string): string {
    return `/api/accounts/${accountId}/repositories`;
  }

}
