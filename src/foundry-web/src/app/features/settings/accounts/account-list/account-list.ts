import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  OutputEmitterRef,
  Signal,
  computed,
  WritableSignal,
  input,
  linkedSignal,
  output,
} from '@angular/core';
import {
  AccountProblem,
  AccountStatusTag,
  AccountSummary,
  ResolvedTokenExpiry,
  TokenLink,
  accountProblem,
  accountStatusTag,
  isTokenExpiryWarning,
  providerDisplayName,
  resolveTokenExpiry,
  resolveTokenStatus,
} from '../account.model';
import { RouterLink } from '@angular/router';
import { TokenExpiryComponent } from '../token-expiry/token-expiry';
import { resolveHost, sortNamespaces } from '../../account-label.util';
import { ProviderIconComponent } from '../../../../shared/components/provider-icon/provider-icon';
import { SpinnerComponent } from '../../../../shared/components/spinner/spinner';

const VISIBLE_NAMESPACE_COUNT = 3;
const VISIBLE_REPOSITORY_COUNT = 3;

interface AccountRowView {
  readonly account: AccountSummary;
  readonly host: string;
  readonly namespaces: readonly string[];
  readonly visibleNamespaces: readonly string[];
  readonly hiddenCount: number;
  readonly status: AccountStatusTag | null;
  readonly problem: AccountProblem | null;
  /** The muted expiry line: shown for a present token whose expiry needs no warning. */
  readonly expiryLine: ResolvedTokenExpiry | null;
  readonly providerName: string;
}

@Component({
  selector: 'fd-account-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ProviderIconComponent, RouterLink, SpinnerComponent, TokenExpiryComponent],
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
      </div>
    }

    @if (!loading() && !error() && accounts().length > 0) {
      <ul class="account-list__list" role="list" aria-label="Accounts">
        @for (row of _rows(); track row.account.id) {
          @let expanded = _isExpanded(row.account.id);
          @let busy = deletingAccountId() === row.account.id;
          <li class="account-list__row" role="listitem">
            <fd-provider-icon class="account-list__provider" [providerType]="row.account.providerType" />

            <div class="account-list__main">
              <div class="account-list__title-line">
                <span class="account-list__name">{{ row.account.name }}</span>
                @if (row.status; as status) {
                  <span class="account-list__status account-list__status--inline account-list__status--{{ status.tone }}">{{ status.label }}</span>
                }
              </div>
              <p class="account-list__meta">
                <span class="account-list__namespaces" [id]="'account-namespaces-' + row.account.id">
                  <span class="account-list__host">{{ row.host }}</span>
                  @if (row.namespaces.length > 0) {
                    <span class="account-list__namespace-text">{{ (expanded ? row.namespaces : row.visibleNamespaces).join(', ') }}</span>
                  }
                </span>
                @if (row.hiddenCount > 0) {
                  <button
                    type="button"
                    class="account-list__more-btn"
                    [attr.aria-expanded]="expanded"
                    [attr.aria-controls]="'account-namespaces-' + row.account.id"
                    [attr.aria-label]="expanded
                      ? 'Show fewer namespaces for ' + row.account.name
                      : '+' + row.hiddenCount + ' more namespaces for ' + row.account.name"
                    (click)="_toggleNamespaces(row.account.id)"
                  >{{ expanded ? 'Show fewer' : '+' + row.hiddenCount + ' more' }}</button>
                }
              </p>
              @if (row.expiryLine; as expiry) {
                <p class="account-list__expiry"><fd-token-expiry [expiry]="expiry" [providerName]="row.providerName" /></p>
              }
            </div>

            <div class="account-list__side">
              @if (row.status; as status) {
                <span class="account-list__status account-list__status--side account-list__status--{{ status.tone }}">{{ status.label }}</span>
              }
              <button
                type="button"
                class="account-list__icon-btn account-list__edit-btn"
                [attr.aria-label]="'Edit account ' + row.account.name"
                [attr.title]="'Edit account ' + row.account.name"
                [attr.aria-disabled]="busy ? 'true' : null"
                (click)="_onEdit(row.account)"
              >
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
                     stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                  <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                  <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                </svg>
              </button>
              <button
                type="button"
                class="account-list__icon-btn account-list__delete-btn"
                [attr.aria-label]="busy ? 'Deleting account ' + row.account.name + '…' : 'Delete account ' + row.account.name"
                [attr.title]="busy ? 'Deleting account ' + row.account.name + '…' : 'Delete account ' + row.account.name"
                [disabled]="busy"
                (click)="_onDelete(row.account)"
              >
                @if (busy) {
                  <fd-spinner />
                } @else {
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
                       stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                    <polyline points="3 6 5 6 21 6" />
                    <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
                    <path d="M10 11v6" />
                    <path d="M14 11v6" />
                    <path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                  </svg>
                }
              </button>
            </div>

            @if (row.problem; as problem) {
              @if (problem.kind === 'missing-permissions') {
                @let reposExpanded = _isReposExpanded(row.account.id);
                @let hiddenRepoCount = problem.repositories.length - visibleRepositoryCount;
                <div
                  class="account-list__problem account-list__problem--warning"
                  role="group"
                  [attr.aria-label]="'Token permissions for ' + row.account.name"
                >
                  <p class="account-list__problem-text">@if (problem.expiry; as expiry) {<span class="sr-only">Warning: </span><fd-token-expiry [expiry]="expiry" [providerName]="row.providerName" />. }{{ problem.message }}</p>
                  @for (reason of problem.reasons; track reason) {
                    <p class="account-list__problem-text">{{ reason }}</p>
                  }
                  @if (problem.missingPermissions.length > 0) {
                    <p class="account-list__problem-text">Missing: {{ problem.missingPermissions.join('; ') }}.</p>
                  }
                  @if (problem.repositories.length > 0) {
                    <p class="account-list__problem-text">
                      <span [id]="'account-denied-repos-' + row.account.id">Repositories:
                        @for (repository of (reposExpanded ? problem.repositories : problem.repositories.slice(0, visibleRepositoryCount)); track repository.id; let last = $last) {
                          <a class="account-list__repo-link" [routerLink]="['/settings/repositories', repository.id]">{{ repository.slug }}</a>@if (!last) {<span>, </span>}
                        }
                      </span>
                      @if (hiddenRepoCount > 0) {
                        <button
                          type="button"
                          class="account-list__more-btn"
                          [attr.aria-expanded]="reposExpanded"
                          [attr.aria-controls]="'account-denied-repos-' + row.account.id"
                          [attr.aria-label]="reposExpanded
                            ? 'Show fewer repositories for ' + row.account.name
                            : '+' + hiddenRepoCount + ' more repositories for ' + row.account.name"
                          (click)="_toggleRepositories(row.account.id)"
                        >{{ reposExpanded ? 'Show fewer' : '+' + hiddenRepoCount + ' more' }}</button>
                      }
                    </p>
                  }
                  <p class="account-list__problem-text">{{ problem.remedyText }}</p>
                  <div class="account-list__problem-actions">
                    @if (problem.tokenLink; as tokenLink) {
                      @if (problem.expiry) {
                        <button
                          type="button"
                          class="account-list__btn account-list__btn--primary account-list__problem-btn"
                          [attr.aria-label]="'Replace token for ' + row.account.name"
                          (click)="edit.emit(row.account)"
                        >Replace token</button>
                      }
                      <a
                        class="account-list__btn account-list__link-btn account-list__problem-btn"
                        [class.account-list__btn--primary]="!problem.expiry"
                        [href]="tokenLink.url"
                        target="_blank"
                        rel="noopener noreferrer"
                        [attr.aria-label]="_tokenLinkVerb(tokenLink) + ' token on ' + tokenLink.host + ' for ' + row.account.name + ' (opens in a new tab)'"
                      >{{ _tokenLinkVerb(tokenLink) }} token on {{ tokenLink.host }} <span aria-hidden="true">↗</span><span class="sr-only">(opens in a new tab)</span></a>
                      @if (!problem.expiry) {
                        <button
                          type="button"
                          class="account-list__btn account-list__problem-btn"
                          [attr.aria-label]="'Replace token for ' + row.account.name"
                          (click)="edit.emit(row.account)"
                        >Replace token</button>
                      }
                    } @else {
                      <button
                        type="button"
                        class="account-list__btn account-list__problem-btn"
                        [attr.aria-label]="'Replace token for ' + row.account.name"
                        (click)="edit.emit(row.account)"
                      >Replace token</button>
                    }
                  </div>
                </div>
              } @else {
              <div
                class="account-list__problem account-list__problem--{{ problem.tone }}"
                role="group"
                [attr.aria-label]="'Token status for ' + row.account.name"
              >
                <p class="account-list__problem-text">@if (problem.expiry; as expiry) {<span class="sr-only">Warning: </span><fd-token-expiry [expiry]="expiry" [providerName]="row.providerName" />. }{{ problem.message }}</p>
                <div class="account-list__problem-actions">
                  <button
                    type="button"
                    class="account-list__btn account-list__btn--primary account-list__problem-btn"
                    [attr.aria-label]="problem.actionLabel + ' for ' + row.account.name"
                    (click)="edit.emit(row.account)"
                  >{{ problem.actionLabel }}</button>
                </div>
              </div>
              }
            }
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
  readonly deletingAccountId: InputSignal<string | null> = input<string | null>(null);

  readonly edit: OutputEmitterRef<AccountSummary> = output<AccountSummary>();
  readonly delete: OutputEmitterRef<AccountSummary> = output<AccountSummary>();
  readonly retry: OutputEmitterRef<void> = output<void>();

  protected readonly _rows: Signal<AccountRowView[]> = computed(() =>
    this.accounts().map(account => {
      const namespaces = sortNamespaces(account.namespaces);
      const expiry = resolveTokenExpiry(account.tokenExpiry);
      return {
        account,
        host: resolveHost(account.baseUrl),
        namespaces,
        visibleNamespaces: namespaces.slice(0, VISIBLE_NAMESPACE_COUNT),
        hiddenCount: Math.max(0, namespaces.length - VISIBLE_NAMESPACE_COUNT),
        status: accountStatusTag(account),
        problem: accountProblem(account),
        expiryLine: resolveTokenStatus(account) === 'present' && !isTokenExpiryWarning(expiry) ? expiry : null,
        providerName: providerDisplayName(account.providerType),
      };
    }),
  );

  // Keyed by account id: a reload of the same account keeps its expansion, and an account
  // that leaves the list drops its entry.
  private readonly _expandedIds: WritableSignal<ReadonlySet<string>> = linkedSignal<AccountSummary[], ReadonlySet<string>>({
    source: this.accounts,
    computation: (accounts, previous) => {
      const present = new Set(accounts.map(a => a.id));
      return new Set([...(previous?.value ?? [])].filter(id => present.has(id)));
    },
  });

  protected readonly visibleRepositoryCount = VISIBLE_REPOSITORY_COUNT;

  // Denied-repository expansion is kept apart from namespace expansion, keyed the same way.
  private readonly _expandedRepoIds: WritableSignal<ReadonlySet<string>> = linkedSignal<AccountSummary[], ReadonlySet<string>>({
    source: this.accounts,
    computation: (accounts, previous) => {
      const present = new Set(accounts.map(a => a.id));
      return new Set([...(previous?.value ?? [])].filter(id => present.has(id)));
    },
  });

  protected _tokenLinkVerb(link: TokenLink): string {
    return link.mode === 'edit' ? 'Edit' : 'Create';
  }

  protected _isReposExpanded(id: string): boolean {
    return this._expandedRepoIds().has(id);
  }

  protected _toggleRepositories(id: string): void {
    const next = new Set(this._expandedRepoIds());
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    this._expandedRepoIds.set(next);
  }

  protected _isExpanded(id: string): boolean {
    return this._expandedIds().has(id);
  }

  protected _toggleNamespaces(id: string): void {
    const next = new Set(this._expandedIds());
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    this._expandedIds.set(next);
  }

  protected _onEdit(account: AccountSummary): void {
    if (this.deletingAccountId() === account.id) {
      return;
    }
    this.edit.emit(account);
  }

  protected _onDelete(account: AccountSummary): void {
    if (this.deletingAccountId() === account.id) {
      return;
    }
    this.delete.emit(account);
  }
}
