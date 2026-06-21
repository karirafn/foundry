import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, input, output } from '@angular/core';
import { AccountSummary } from '../account.model';
import { ProviderIconComponent } from '../../../../shared/components/provider-icon/provider-icon';

@Component({
  selector: 'fd-account-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ProviderIconComponent],
  template: `
    @if (error()) {
      <div class="account-list__error" role="alert">
        <span class="account-list__error-message">{{ error() }}</span>
        <button
          class="account-list__retry-btn"
          type="button"
          (click)="retry.emit()"
        >Retry</button>
      </div>
    }

    @if (loading()) {
      <div class="account-list__loading" role="status" aria-label="Loading accounts">
        <span class="sr-only">Loading accounts</span>
      </div>
    }

    @if (!loading() && !error() && accounts().length === 0) {
      <div class="account-list__empty" role="status">
        <p class="account-list__empty-heading">No accounts configured</p>
        <p class="account-list__empty-description">
          Add your first provider account to start monitoring repositories.
        </p>
        <button
          class="account-list__add-btn"
          type="button"
          (click)="add.emit()"
        >+ Add Account</button>
      </div>
    }

    @if (!loading() && !error() && accounts().length > 0) {
      <div class="account-list__header">
        <span></span>
        <button
          class="account-list__add-btn"
          type="button"
          (click)="add.emit()"
        >+ Add Account</button>
      </div>
      <ul class="account-list__list" role="list">
        @for (account of accounts(); track account.id) {
          <li class="account-list__item" role="listitem">
            <fd-provider-icon [providerType]="account.providerType" />
            <div class="account-list__info">
              <span class="account-list__name">{{ account.name }}</span>
              <span class="account-list__url">{{ account.baseUrl }}</span>
            </div>
            <div class="account-list__token-status">
              <span
                class="account-list__token-dot account-list__token-dot--{{ account.hasToken ? 'configured' : 'not-configured' }}"
                aria-hidden="true"
              ></span>
              <span class="account-list__token-label account-list__token-label--{{ account.hasToken ? 'configured' : 'not-configured' }}">
                {{ account.hasToken ? 'Configured' : 'Not configured' }}
              </span>
            </div>
            <div class="account-list__actions">
              <button
                class="account-list__edit-btn"
                type="button"
                [attr.aria-label]="'Edit account ' + account.name"
                (click)="edit.emit(account)"
              >Edit</button>
              <button
                class="account-list__delete-btn"
                type="button"
                [attr.aria-label]="'Delete account ' + account.name"
                (click)="delete.emit(account)"
              >Delete</button>
            </div>
          </li>
        }
      </ul>
    }
  `,
  styleUrl: './account-list.scss',
})
export class AccountListComponent {
  readonly accounts: InputSignal<AccountSummary[]> = input<AccountSummary[]>([]);
  readonly loading: InputSignal<boolean> = input<boolean>(false);
  readonly error: InputSignal<string | null> = input<string | null>(null);

  readonly add: OutputEmitterRef<void> = output<void>();
  readonly edit: OutputEmitterRef<AccountSummary> = output<AccountSummary>();
  readonly delete: OutputEmitterRef<AccountSummary> = output<AccountSummary>();
  readonly retry: OutputEmitterRef<void> = output<void>();
}
