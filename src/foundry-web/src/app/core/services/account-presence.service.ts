import { Injectable, Signal, WritableSignal, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class AccountPresenceService {
  private readonly _hasAccountsSignal: WritableSignal<boolean> = signal(false);
  readonly hasAccounts: Signal<boolean> = this._hasAccountsSignal.asReadonly();

  setHasAccounts(hasAccounts: boolean): void {
    this._hasAccountsSignal.set(hasAccounts);
  }
}
