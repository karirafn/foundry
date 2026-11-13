import { Injectable, Signal, computed, effect, inject, untracked } from '@angular/core';
import { LoadOutcome } from '../../core/models/load-outcome';
import { IssueService } from '../issues/issue.service';
import { AccountService } from '../settings/accounts/account.service';
import { RepositoryService } from '../settings/repositories/repository.service';

/**
 * Keeps the repository list the board's repository alerts read fresh: loaded with the page, and
 * reloaded when a queued issue's repository becomes ineligible, since that can mean the violations
 * changed. After a reconnect the connection gate reloads it with the rest of the board. Provided per
 * board page.
 */
@Injectable()
export class BoardRepositoriesService {
  private readonly _accounts = inject(AccountService);
  private readonly _repositories = inject(RepositoryService);
  private readonly _issues = inject(IssueService);

  /** The repositories holding queued issues because they are ineligible, as a stable key. */
  private readonly _heldKey: Signal<string> = computed(() =>
    [
      ...new Set(
        this._issues
          .issues()
          .filter((i) => i.awaitsDispatch && i.repositoryEligibilityStatus === 'ineligible')
          .map((i) => i.repositoryId),
      ),
    ]
      .sort()
      .join(','),
  );

  constructor() {
    let lastHeldKey: string | null = null;
    effect(() => {
      const key = this._heldKey();
      if (this._issues.initialLoading()) {
        return;
      }
      if (lastHeldKey !== null && key !== lastHeldKey && key !== '') {
        untracked(() => void this.reload());
      }
      lastHeldKey = key;
    });
  }

  /** Loads the accounts, then every account's repositories. */
  async reload(): Promise<LoadOutcome> {
    if ((await this._accounts.loadAccounts()) === 'failed') {
      return 'failed';
    }
    return this._repositories.loadAllRepositories(this._accounts.accounts().map((a) => a.id));
  }
}
