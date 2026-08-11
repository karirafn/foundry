import { Injectable, Signal, WritableSignal, effect, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AccountSummary, AffectedRepository, CreateAccountRequest, CredentialCreationResult, CredentialUpdateResult, NamespaceConflict, NamespaceConflictResponse, ProviderType, TakeoverValidationResponse, TokenRequirements, TokenValidationResult, UpdateAccountRequest, ValidateTokenRequest } from './account.model';
import { ToastService } from '../../../core/services/toast.service';
import { AccountPresenceService } from '../../../core/services/account-presence.service';

const API_BASE = '/api/accounts';
const PROVIDERS_API_BASE = '/api/providers';
const TOAST_ALL_RETAINED = 'Token updated. All repositories retained their access.';

@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly _http = inject(HttpClient);
  private readonly _toastService = inject(ToastService);
  private readonly _accountPresence = inject(AccountPresenceService);

  private readonly _accountsSignal: WritableSignal<AccountSummary[]> = signal([]);
  readonly accounts: Signal<AccountSummary[]> = this._accountsSignal.asReadonly();

  private readonly _loadingSignal: WritableSignal<boolean> = signal(false);
  readonly loading: Signal<boolean> = this._loadingSignal.asReadonly();

  private readonly _savingSignal: WritableSignal<boolean> = signal(false);
  readonly saving: Signal<boolean> = this._savingSignal.asReadonly();

  private readonly _deletingSignal: WritableSignal<boolean> = signal(false);
  readonly deleting: Signal<boolean> = this._deletingSignal.asReadonly();

  private readonly _validatingSignal: WritableSignal<boolean> = signal(false);
  readonly validating: Signal<boolean> = this._validatingSignal.asReadonly();

  private readonly _saveSuccessSignal: WritableSignal<boolean> = signal(false);
  readonly saveSuccess: Signal<boolean> = this._saveSuccessSignal.asReadonly();

  private readonly _validationResultSignal: WritableSignal<TokenValidationResult | null> = signal(null);
  readonly validationResult: Signal<TokenValidationResult | null> = this._validationResultSignal.asReadonly();

  private readonly _saveErrorSignal: WritableSignal<string | null> = signal(null);
  readonly saveError: Signal<string | null> = this._saveErrorSignal.asReadonly();

  private readonly _deleteErrorSignal: WritableSignal<string | null> = signal(null);
  readonly deleteError: Signal<string | null> = this._deleteErrorSignal.asReadonly();

  private readonly _loadErrorSignal: WritableSignal<string | null> = signal(null);
  readonly loadError: Signal<string | null> = this._loadErrorSignal.asReadonly();

  private readonly _validationErrorSignal: WritableSignal<string | null> = signal(null);
  readonly validationError: Signal<string | null> = this._validationErrorSignal.asReadonly();

  private readonly _affectedRepositoriesSignal: WritableSignal<AffectedRepository[] | null> = signal(null);
  readonly affectedRepositories: Signal<AffectedRepository[] | null> = this._affectedRepositoriesSignal.asReadonly();

  private readonly _srAnnouncementSignal: WritableSignal<string> = signal('');
  readonly srAnnouncement: Signal<string> = this._srAnnouncementSignal.asReadonly();

  private readonly _conflictsSignal: WritableSignal<NamespaceConflict[]> = signal([]);
  readonly conflicts: Signal<NamespaceConflict[]> = this._conflictsSignal.asReadonly();

  private readonly _tokenRequirementsCache = new Map<ProviderType, Promise<TokenRequirements>>();

  constructor() {
    effect(() => this._accountPresence.setHasAccounts(this.accounts().length > 0));
  }

  loadAccounts(): Promise<void> {
    this._loadErrorSignal.set(null);
    this._saveSuccessSignal.set(false);
    this._loadingSignal.set(true);

    return new Promise<void>((resolve) => {
      this._http.get<AccountSummary[]>(API_BASE).subscribe({
        next: (accounts) => {
          this._accountsSignal.set(accounts);
          this._loadingSignal.set(false);
          resolve();
        },
        error: (err: HttpErrorResponse) => {
          console.error(err);
          this._loadErrorSignal.set(this._extractErrorMessage(err));
          this._loadingSignal.set(false);
          resolve();
        },
      });
    });
  }

  createAccount(request: CreateAccountRequest): void {
    this._saveErrorSignal.set(null);
    this._saveSuccessSignal.set(false);
    this._affectedRepositoriesSignal.set(null);
    this._conflictsSignal.set([]);
    this._savingSignal.set(true);

    this._http.post<CredentialCreationResult>(API_BASE, request).subscribe({
      next: (result) => {
        this._accountsSignal.update(accounts => [...accounts, result.credential]);
        this._affectedRepositoriesSignal.set(result.affectedRepositories);
        if (result.affectedRepositories.length > 0) {
          this._srAnnouncementSignal.set(
            `Account added. ${result.affectedRepositories.length} repositories affected — review below.`
          );
        }
        this._savingSignal.set(false);
        this._saveSuccessSignal.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        if (err.status === 409 && this._isNamespaceConflictResponse(err.error)) {
          const conflictBody = err.error as NamespaceConflictResponse;
          this._conflictsSignal.set(conflictBody.conflicts);
          this._srAnnouncementSignal.set(
            `${conflictBody.conflicts.length} namespace conflict${conflictBody.conflicts.length === 1 ? '' : 's'} — select namespaces to transfer and retry.`
          );
          this._savingSignal.set(false);
          return;
        }
        if (err.status === 422 && this._isTakeoverValidationResponse(err.error)) {
          const body = err.error as TakeoverValidationResponse;
          this._saveErrorSignal.set(`Invalid namespaces for takeover: ${body.invalidNamespaces.join(', ')}.`);
          this._savingSignal.set(false);
          this._saveSuccessSignal.set(false);
          return;
        }
        this._saveErrorSignal.set(this._extractErrorMessage(err));
        this._savingSignal.set(false);
        this._saveSuccessSignal.set(false);
      },
    });
  }

  updateAccount(id: string, request: UpdateAccountRequest): void {
    this._saveErrorSignal.set(null);
    this._saveSuccessSignal.set(false);
    this._affectedRepositoriesSignal.set(null);
    this._savingSignal.set(true);

    this._http.put<CredentialUpdateResult>(`${API_BASE}/${id}`, request).subscribe({
      next: (result) => {
        this._accountsSignal.update(accounts =>
          accounts.map(a => a.id === result.credential.id ? result.credential : a)
        );
        this._affectedRepositoriesSignal.set(result.affectedRepositories);
        if (result.affectedRepositories.length === 0) {
          this._toastService.show(TOAST_ALL_RETAINED);
        } else {
          this._srAnnouncementSignal.set(
            `Token updated. ${result.affectedRepositories.length} repositories affected — review below.`
          );
        }
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

  clearAffectedRepositories(): void {
    this._affectedRepositoriesSignal.set(null);
    this._srAnnouncementSignal.set('');
  }

  deleteAccount(id: string): void {
    this._deleteErrorSignal.set(null);
    this._deletingSignal.set(true);

    this._http.delete(`${API_BASE}/${id}`).subscribe({
      next: () => {
        this._accountsSignal.update(accounts => accounts.filter(a => a.id !== id));
        this._deletingSignal.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._deleteErrorSignal.set(this._extractErrorMessage(err));
        this._deletingSignal.set(false);
      },
    });
  }

  getTokenRequirements(provider: ProviderType): Promise<TokenRequirements> {
    const cached = this._tokenRequirementsCache.get(provider);
    if (cached !== undefined) {
      return cached;
    }

    const request = firstValueFrom(
      this._http.get<TokenRequirements>(`${PROVIDERS_API_BASE}/${provider.toLowerCase()}/token-requirements`)
    ).catch((err: HttpErrorResponse) => {
      this._tokenRequirementsCache.delete(provider);
      return Promise.reject(err);
    });

    this._tokenRequirementsCache.set(provider, request);
    return request;
  }

  private _extractErrorMessage(err: HttpErrorResponse): string {
    if (typeof err.error === 'string' && err.error) {
      return err.error;
    }
    return err.message;
  }

  private _isNamespaceConflictResponse(body: unknown): boolean {
    return (
      typeof body === 'object' &&
      body !== null &&
      'conflicts' in body &&
      Array.isArray((body as NamespaceConflictResponse).conflicts)
    );
  }

  private _isTakeoverValidationResponse(body: unknown): boolean {
    return (
      typeof body === 'object' &&
      body !== null &&
      'invalidNamespaces' in body &&
      Array.isArray((body as TakeoverValidationResponse).invalidNamespaces)
    );
  }

  validateToken(request: ValidateTokenRequest): void {
    this._validationResultSignal.set(null);
    this._validationErrorSignal.set(null);
    this._validatingSignal.set(true);

    this._http.post<TokenValidationResult>(`${API_BASE}/validate-token`, request).subscribe({
      next: (result) => {
        this._validationResultSignal.set(result);
        this._validatingSignal.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._validationErrorSignal.set(this._extractErrorMessage(err));
        this._validatingSignal.set(false);
      },
    });
  }
}
