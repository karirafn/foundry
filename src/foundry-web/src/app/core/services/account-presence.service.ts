import { Injectable, Signal, WritableSignal, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class AccountPresenceService {
  private readonly _hasAccountsSignal: WritableSignal<boolean> = signal(false);
  readonly hasAccounts: Signal<boolean> = this._hasAccountsSignal.asReadonly();

  /**
   * Sole writer entry point — the accounts feature (AccountService) mirrors
   * account presence here so core consumers (SettingsService) can read it
   * without depending on the feature layer. Do not call from elsewhere.
   */
  setHasAccounts(hasAccounts: boolean): void {
    this._hasAccountsSignal.set(hasAccounts);
  }
}
