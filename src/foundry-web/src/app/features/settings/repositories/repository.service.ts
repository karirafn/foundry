import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { AvailableRepository, CreateRepositoryRequest, RepositorySummary, UpdateRepositoryRequest } from './repository.model';

@Injectable({ providedIn: 'root' })
export class RepositoryService {
  private readonly _http = inject(HttpClient);

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

  private readonly _deleteErrorSignal: WritableSignal<string | null> = signal(null);
  readonly deleteError: Signal<string | null> = this._deleteErrorSignal.asReadonly();

  private readonly _availableRepositoriesSignal: WritableSignal<AvailableRepository[]> = signal([]);
  readonly availableRepositories: Signal<AvailableRepository[]> = this._availableRepositoriesSignal.asReadonly();

  private readonly _loadingAvailableSignal: WritableSignal<boolean> = signal(false);
  readonly loadingAvailable: Signal<boolean> = this._loadingAvailableSignal.asReadonly();

  private readonly _loadAvailableErrorSignal: WritableSignal<string | null> = signal(null);
  readonly loadAvailableError: Signal<string | null> = this._loadAvailableErrorSignal.asReadonly();

  loadRepositories(accountId: string): void {
    this._loadErrorSignal.set(null);
    this._loadingSignal.set(true);

    this._http.get<RepositorySummary[]>(this._repositoriesUrl(accountId)).subscribe({
      next: (repositories) => {
        this._repositoriesSignal.set(repositories);
        this._loadingSignal.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._loadErrorSignal.set(this._extractErrorMessage(err));
        this._loadingSignal.set(false);
      },
    });
  }

  loadAvailableRepositories(accountId: string): void {
    this._loadAvailableErrorSignal.set(null);
    this._loadingAvailableSignal.set(true);

    this._http.get<AvailableRepository[]>(`${this._repositoriesUrl(accountId)}/available-repositories`).subscribe({
      next: (repositories) => {
        this._availableRepositoriesSignal.set(repositories);
        this._loadingAvailableSignal.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._loadAvailableErrorSignal.set(this._extractErrorMessage(err));
        this._loadingAvailableSignal.set(false);
      },
    });
  }

  createRepository(accountId: string, request: CreateRepositoryRequest): void {
    this._saveErrorSignal.set(null);
    this._saveSuccessSignal.set(false);
    this._savingSignal.set(true);

    this._http.post<RepositorySummary>(this._repositoriesUrl(accountId), request).subscribe({
      next: (repository) => {
        this._repositoriesSignal.update(repositories => [...repositories, repository]);
        this._savingSignal.set(false);
        this._saveSuccessSignal.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._saveErrorSignal.set(this._extractErrorMessage(err));
        this._savingSignal.set(false);
        this._saveSuccessSignal.set(false);
      },
    });
  }

  updateRepository(accountId: string, id: string, request: UpdateRepositoryRequest): void {
    this._saveErrorSignal.set(null);
    this._saveSuccessSignal.set(false);
    this._savingSignal.set(true);

    this._http.put<RepositorySummary>(`${this._repositoriesUrl(accountId)}/${id}`, request).subscribe({
      next: (updated) => {
        this._repositoriesSignal.update(repositories =>
          repositories.map(r => r.id === updated.id ? updated : r)
        );
        this._savingSignal.set(false);
        this._saveSuccessSignal.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._saveErrorSignal.set(this._extractErrorMessage(err));
        this._savingSignal.set(false);
        this._saveSuccessSignal.set(false);
      },
    });
  }

  deleteRepository(accountId: string, id: string): void {
    this._deleteErrorSignal.set(null);
    this._deletingSignal.set(true);

    this._http.delete(`${this._repositoriesUrl(accountId)}/${id}`).subscribe({
      next: () => {
        this._repositoriesSignal.update(repositories => repositories.filter(r => r.id !== id));
        this._deletingSignal.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._deleteErrorSignal.set(this._extractErrorMessage(err));
        this._deletingSignal.set(false);
      },
    });
  }

  private _repositoriesUrl(accountId: string): string {
    return `/api/accounts/${accountId}/repositories`;
  }

  private _extractErrorMessage(err: HttpErrorResponse): string {
    if (typeof err.error === 'string' && err.error) {
      return err.error;
    }
    return err.message;
  }
}
