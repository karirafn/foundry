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

  readonly accounts: WritableSignal<AccountSummary[]> = signal([]);
  readonly loading: WritableSignal<boolean> = signal(false);
  readonly saving: WritableSignal<boolean> = signal(false);
  readonly deleting: WritableSignal<boolean> = signal(false);
  readonly validating: WritableSignal<boolean> = signal(false);
  readonly saveSuccess: WritableSignal<boolean> = signal(false);

  private readonly _validationResultSignal: WritableSignal<TokenValidationResult | null> = signal(null);
  readonly validationResult: Signal<TokenValidationResult | null> = this._validationResultSignal.asReadonly();

  private readonly _saveErrorSignal: WritableSignal<string | null> = signal(null);
  readonly saveError: Signal<string | null> = this._saveErrorSignal.asReadonly();

  private readonly _deleteErrorSignal: WritableSignal<string | null> = signal(null);
  readonly deleteError: Signal<string | null> = this._deleteErrorSignal.asReadonly();

  private readonly _loadErrorSignal: WritableSignal<string | null> = signal(null);
  readonly loadError: Signal<string | null> = this._loadErrorSignal.asReadonly();

  loadAccounts(): void {
    this._loadErrorSignal.set(null);
    this.saveSuccess.set(false);
    this.loading.set(true);

    this._http.get<AccountSummary[]>(API_BASE).subscribe({
      next: (accounts) => {
        this.accounts.set(accounts);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._loadErrorSignal.set(this._extractErrorMessage(err));
        this.loading.set(false);
      },
    });
  }

  createAccount(request: CreateAccountRequest): void {
    this._saveErrorSignal.set(null);
    this.saveSuccess.set(false);
    this.saving.set(true);

    this._http.post<AccountSummary>(API_BASE, request).subscribe({
      next: (account) => {
        this.accounts.update(accounts => [...accounts, account]);
        this.saving.set(false);
        this.saveSuccess.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._saveErrorSignal.set(this._extractErrorMessage(err));
        this.saving.set(false);
        this.saveSuccess.set(false);
      },
    });
  }

  updateAccount(id: string, request: UpdateAccountRequest): void {
    this._saveErrorSignal.set(null);
    this.saveSuccess.set(false);
    this.saving.set(true);

    this._http.put<AccountSummary>(`${API_BASE}/${id}`, request).subscribe({
      next: (updated) => {
        this.accounts.update(accounts =>
          accounts.map(a => a.id === updated.id ? updated : a)
        );
        this.saving.set(false);
        this.saveSuccess.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._saveErrorSignal.set(this._extractErrorMessage(err));
        this.saving.set(false);
        this.saveSuccess.set(false);
      },
    });
  }

  deleteAccount(id: string): void {
    this._deleteErrorSignal.set(null);
    this.deleting.set(true);

    this._http.delete(`${API_BASE}/${id}`).subscribe({
      next: () => {
        this.accounts.update(accounts => accounts.filter(a => a.id !== id));
        this.deleting.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._deleteErrorSignal.set(this._extractErrorMessage(err));
        this.deleting.set(false);
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
    this.validating.set(true);

    this._http.post<TokenValidationResult>(`${API_BASE}/validate-token`, request).subscribe({
      next: (result) => {
        this._validationResultSignal.set(result);
        this.validating.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this.validating.set(false);
      },
    });
  }
}
