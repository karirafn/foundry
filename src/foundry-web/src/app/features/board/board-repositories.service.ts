import { Injectable, Signal, computed, effect, inject, untracked } from '@angular/core';
import { IssueSignalRService } from '../../core/services/issue-signalr.service';
import { IssueService } from '../issues/issue.service';
import { AccountService } from '../settings/accounts/account.service';
import { RepositoryService } from '../settings/repositories/repository.service';

/**
 * Keeps the repository list the board's repository alerts read fresh: loaded with the page, and
 * reloaded when a queued issue's repository becomes ineligible and after a reconnect, since either
 * can mean the violations changed. Provided per board page.
 */
@Injectable()
export class BoardRepositoriesService {
  private readonly _accounts = inject(AccountService);
  private readonly _repositories = inject(RepositoryService);
  private readonly _issues = inject(IssueService);
  private readonly _connectionStatus = inject(IssueSignalRService).connectionStatus;

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
        untracked(() => this.reload());
      }
      lastHeldKey = key;
    });

    let wasConnected = true;
    effect(() => {
      const connected = this._connectionStatus() === 'connected';
      if (connected && !wasConnected) {
        untracked(() => this.reload());
      }
      wasConnected = connected;
    });
  }

  /** Loads the accounts, then every account's repositories. */
  reload(): void {
    void this._accounts.loadAccounts().then(() => {
      this._repositories.loadAllRepositories(this._accounts.accounts().map((a) => a.id));
    });
  }
}
