import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { AccountSummary, CreateAccountRequest, TokenValidationResult, UpdateAccountRequest } from './account.model';

const API_BASE = '/api/accounts';

interface ValidateTokenRequest {
  token: string;
  baseUrl: string;
}

@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly _http = inject(HttpClient);

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

  loadAccounts(): void {
    this._loadErrorSignal.set(null);
    this._saveSuccessSignal.set(false);
    this._loadingSignal.set(true);

    this._http.get<AccountSummary[]>(API_BASE).subscribe({
      next: (accounts) => {
        this._accountsSignal.set(accounts);
        this._loadingSignal.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._loadErrorSignal.set(this._extractErrorMessage(err));
        this._loadingSignal.set(false);
      },
    });
  }

  createAccount(request: CreateAccountRequest): void {
    this._saveErrorSignal.set(null);
    this._saveSuccessSignal.set(false);
    this._savingSignal.set(true);

    this._http.post<AccountSummary>(API_BASE, request).subscribe({
      next: (account) => {
        this._accountsSignal.update(accounts => [...accounts, account]);
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

  updateAccount(id: string, request: UpdateAccountRequest): void {
    this._saveErrorSignal.set(null);
    this._saveSuccessSignal.set(false);
    this._savingSignal.set(true);

    this._http.put<AccountSummary>(`${API_BASE}/${id}`, request).subscribe({
      next: (updated) => {
        this._accountsSignal.update(accounts =>
          accounts.map(a => a.id === updated.id ? updated : a)
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

  private _extractErrorMessage(err: HttpErrorResponse): string {
    if (typeof err.error === 'string' && err.error) {
      return err.error;
    }
    return err.message;
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
