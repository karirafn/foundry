import { Component, ChangeDetectionStrategy, OnInit, Signal, WritableSignal, computed, effect, inject, signal, Injector, ViewChild, ElementRef, afterNextRender, runInInjectionContext } from '@angular/core';
import { AccountService } from './account.service';
import { AccountListComponent } from './account-list/account-list';
import { AccountFormComponent } from './account-form/account-form';
import { AccountSummary, CreateAccountRequest, UpdateAccountRequest } from './account.model';

type AccountView = { kind: 'list' } | { kind: 'add' } | { kind: 'edit'; account: AccountSummary };

@Component({
  selector: 'fd-settings-accounts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AccountListComponent, AccountFormComponent],
  template: `
    <div class="accounts-settings">
      <section class="accounts-settings__section">
        <h2 class="accounts-settings__section-title" #sectionHeading tabindex="-1">Accounts</h2>
        <p class="accounts-settings__section-description">
          Manage provider accounts for repository monitoring.
        </p>

        @switch (_accountView().kind) {
          @case ('list') {
            <fd-account-list
              [accounts]="accountService.accounts()"
              [loading]="accountService.loading()"
              [error]="_accountError()"
              (add)="onAddAccount()"
              (edit)="onEditAccount($event)"
              (delete)="onDeleteAccount($event)"
              (retry)="accountService.loadAccounts()"
            />
            <div class="accounts-settings__delete-error" role="alert">
              @if (accountService.deleteError(); as deleteError) {
                {{ deleteError }}
              }
            </div>
          }
          @case ('add') {
            <fd-account-form
              [accounts]="accountService.accounts()"
              [saving]="accountService.saving()"
              [validating]="accountService.validating()"
              [validationResult]="accountService.validationResult()"
              [validationError]="accountService.validationError()"
              [saveError]="accountService.saveError()"
              (save)="onSaveNewAccount($event)"
              (validateToken)="onValidateToken($event)"
              (cancel)="onAccountCancelled()"
            />
          }
          @case ('edit') {
            <fd-account-form
              [account]="_editAccount"
              [accounts]="accountService.accounts()"
              [saving]="accountService.saving()"
              [validating]="accountService.validating()"
              [validationResult]="accountService.validationResult()"
              [validationError]="accountService.validationError()"
              [saveError]="accountService.saveError()"
              (save)="onSaveExistingAccount($event)"
              (validateToken)="onValidateToken($event)"
              (cancel)="onAccountCancelled()"
            />
          }
        }
      </section>
    </div>
  `,
  styleUrl: './settings-accounts.scss',
})
export class SettingsAccountsComponent implements OnInit {
  protected readonly accountService = inject(AccountService);
  private readonly _injector = inject(Injector);

  @ViewChild('sectionHeading') private readonly _sectionHeading?: ElementRef<HTMLElement>;

  protected readonly _accountView: WritableSignal<AccountView> = signal({ kind: 'list' });
  protected readonly _accountError: Signal<string | null> = computed(() => this.accountService.loadError());

  protected get _editAccount(): AccountSummary {
    return (this._accountView() as { kind: 'edit'; account: AccountSummary }).account;
  }

  constructor() {
    effect(() => {
      if (this.accountService.saveSuccess() && this._accountView().kind !== 'list') {
        this._accountView.set({ kind: 'list' });
        this.accountService.loadAccounts();
        runInInjectionContext(this._injector, () => {
          afterNextRender(() => {
            this._sectionHeading?.nativeElement.focus();
          });
        });
      }
    });
  }

  ngOnInit(): void {
    this.accountService.loadAccounts();
  }

  onAddAccount(): void {
    this._accountView.set({ kind: 'add' });
  }

  onEditAccount(account: AccountSummary): void {
    this._accountView.set({ kind: 'edit', account });
  }

  onDeleteAccount(account: AccountSummary): void {
    if (window.confirm(`Delete account "${account.name}"?`)) {
      this.accountService.deleteAccount(account.id);
    }
  }

  onSaveNewAccount(request: CreateAccountRequest | UpdateAccountRequest): void {
    this.accountService.createAccount(request as CreateAccountRequest);
  }

  onSaveExistingAccount(request: CreateAccountRequest | UpdateAccountRequest): void {
    const view = this._accountView() as { kind: 'edit'; account: AccountSummary };
    this.accountService.updateAccount(view.account.id, request as UpdateAccountRequest);
  }

  onValidateToken(event: { token: string; baseUrl: string }): void {
    this.accountService.validateToken(event);
  }

  onAccountCancelled(): void {
    this._accountView.set({ kind: 'list' });
    runInInjectionContext(this._injector, () => {
      afterNextRender(() => {
        this._sectionHeading?.nativeElement.focus();
      });
    });
  }
}
