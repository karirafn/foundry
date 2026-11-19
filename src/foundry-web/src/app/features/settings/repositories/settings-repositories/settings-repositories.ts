import {
  Component,
  ChangeDetectionStrategy,
  OnInit,
  Signal,
  WritableSignal,
  computed,
  effect,
  inject,
  signal,
  Injector,
  ViewChild,
  ElementRef,
  viewChild,
  afterNextRender,
  runInInjectionContext,
} from '@angular/core';
import { AccountService } from '../../accounts/account.service';
import { RepositoryService } from '../repository.service';
import { RepositoryListComponent } from '../repository-list/repository-list';
import { RepositoryFormComponent } from '../repository-form/repository-form';
import { ConfirmDialogComponent } from '../../../../shared/components/confirm-dialog/confirm-dialog';
import {
  RepositorySummary,
  CreateRepositoryRequest,
  CreateRepositoryResponse,
  UpdateRepositoryRequest,
  repositoryDeletionMessage,
} from '../repository.model';

type RepositoryView = { kind: 'list' } | { kind: 'add' };

@Component({
  selector: 'fd-settings-repositories',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RepositoryListComponent, RepositoryFormComponent, ConfirmDialogComponent],
  template: `
    <div class="repositories-settings">
      <section class="repositories-settings__section">
        <header class="repositories-settings__header">
          <div class="repositories-settings__heading-group">
            <h2 class="repositories-settings__section-title" #sectionHeading tabindex="-1">Repositories</h2>
            <p class="repositories-settings__section-description">Foundry claims issues from the top repository first.</p>
          </div>
          @if (_repositoryView().kind === 'list') {
            <div class="repositories-settings__actions">
              @if (_reordering()) {
                <button
                  #doneButton
                  type="button"
                  class="repositories-settings__btn repositories-settings__btn--primary"
                  aria-describedby="repository-reorder-hint"
                  (click)="onDoneReordering()"
                >Done</button>
              } @else {
                @if (_canReorder()) {
                  <button
                    #reorderButton
                    type="button"
                    class="repositories-settings__btn"
                    (click)="onStartReordering()"
                  >Reorder</button>
                }
                <button
                  type="button"
                  class="repositories-settings__btn repositories-settings__btn--primary"
                  (click)="onAdd()"
                >Add repository</button>
              }
            </div>
          }
        </header>

        @switch (_repositoryView().kind) {
          @case ('list') {
            <fd-repository-list
              [repositories]="repositoryService.repositories()"
              [loading]="repositoryService.loading()"
              [error]="repositoryService.loadError()"
              [reordering]="_reordering()"
              (delete)="onDelete($event)"
              (retry)="reloadRepositories()"
            />
            <div class="repositories-settings__delete-error" role="alert">
              @if (repositoryService.deleteError(); as deleteError) {
                {{ deleteError }}
              }
            </div>
            <fd-confirm-dialog
              [open]="_pendingDeleteRepository() !== null"
              title="Delete repository?"
              [message]="_deleteRepositoryMessage()"
              confirmLabel="Delete repository"
              cancelLabel="Keep repository"
              [destructive]="true"
              (confirmed)="confirmDeleteRepository()"
              (dismissed)="cancelDeleteRepository()"
            />
          }
          @case ('add') {
            <fd-repository-form
              [accounts]="accountService.accounts()"
              [availableRepositories]="repositoryService.availableRepositories()"
              [hasClaims]="repositoryService.availableHasClaims()"
              [noPushAccessExplanation]="repositoryService.availableNoPushAccessExplanation()"
              [loadingAvailable]="repositoryService.loadingAvailable()"
              [loadAvailableError]="repositoryService.loadAvailableError()"
              [saving]="repositoryService.saving()"
              [saveError]="repositoryService.saveError()"
              [outcome]="_addOutcome()"
              (save)="onSave($event)"
              (cancel)="onCancel()"
              (done)="onAddDone()"
              (accountSelected)="onAccountSelected($event)"
            />
          }
        }
      </section>
    </div>
  `,
  styleUrl: './settings-repositories.scss',
})
export class SettingsRepositoriesComponent implements OnInit {
  protected readonly accountService = inject(AccountService);
  protected readonly repositoryService = inject(RepositoryService);
  private readonly _injector = inject(Injector);

  @ViewChild('sectionHeading') private readonly _sectionHeading?: ElementRef<HTMLElement>;

  protected readonly _repositoryView: WritableSignal<RepositoryView> = signal({ kind: 'list' });
  protected readonly _addOutcome: WritableSignal<CreateRepositoryResponse | null> = signal(null);
  protected readonly _pendingDeleteRepository: WritableSignal<RepositorySummary | null> = signal(null);
  protected readonly _deleteRepositoryMessage: Signal<string> = computed(() => {
    const repository = this._pendingDeleteRepository();
    return repository ? repositoryDeletionMessage(repository.slug) : '';
  });
  private readonly _selectedAccountId: WritableSignal<string> = signal('');
  private readonly _accountIdsKey: Signal<string> = computed(() =>
    this.accountService.accounts().map(a => a.id).sort().join(',')
  );
  private readonly _lastLoadedAccountIdsKey: WritableSignal<string> = signal('');

  private readonly _reorderRequested: WritableSignal<boolean> = signal(false);
  protected readonly _canReorder: Signal<boolean> = computed(() =>
    this._repositoryView().kind === 'list'
    && !this.repositoryService.loading()
    && this.repositoryService.loadError() === null
    && this.repositoryService.repositories().length >= 2);
  // Leaves reorder mode by itself when the list drops below two rows, fails to load, or switches view.
  protected readonly _reordering: Signal<boolean> = computed(() => this._reorderRequested() && this._canReorder());
  private readonly _doneButton = viewChild<ElementRef<HTMLButtonElement>>('doneButton');
  private readonly _reorderButton = viewChild<ElementRef<HTMLButtonElement>>('reorderButton');

  constructor() {
    effect(() => {
      const key = this._accountIdsKey();
      if (key !== this._lastLoadedAccountIdsKey()) {
        this._lastLoadedAccountIdsKey.set(key);
        const accountIds = this.accountService.accounts().map(a => a.id);
        this.repositoryService.loadAllRepositories(accountIds);
      }
    });
  }

  ngOnInit(): void {
    this.accountService.loadAccounts();
  }

  reloadRepositories(): void {
    const accountIds = this.accountService.accounts().map(a => a.id);
    this.repositoryService.loadAllRepositories(accountIds);
  }

  onStartReordering(): void {
    this._reorderRequested.set(true);
    this._focusAfterRender(() => this._doneButton()?.nativeElement);
  }

  onDoneReordering(): void {
    this._reorderRequested.set(false);
    this._focusAfterRender(() => this._reorderButton()?.nativeElement);
  }

  onAdd(): void {
    this._reorderRequested.set(false);
    this._addOutcome.set(null);
    this._repositoryView.set({ kind: 'add' });
  }

  onCancel(): void {
    this._returnToList();
  }

  onAddDone(): void {
    this._returnToList();
  }

  onSave(request: CreateRepositoryRequest | UpdateRepositoryRequest): void {
    this.repositoryService.createRepository(this._selectedAccountId(), request as CreateRepositoryRequest)
      .subscribe({
        next: (response) => this._afterSave(response),
        error: () => { /* handled via saveError signal */ },
      });
  }

  onDelete(repository: RepositorySummary): void {
    this._pendingDeleteRepository.set(repository);
  }

  confirmDeleteRepository(): void {
    const repository = this._pendingDeleteRepository();
    this._pendingDeleteRepository.set(null);
    if (!repository) {
      return;
    }
    this.repositoryService.deleteRepository(repository.accountId, repository.id)
      .subscribe({
        next: () => this._reloadAll(),
        error: () => { /* handled via deleteError signal */ },
      });
  }

  cancelDeleteRepository(): void {
    this._pendingDeleteRepository.set(null);
  }

  onAccountSelected(accountId: string): void {
    this._selectedAccountId.set(accountId);
    this.repositoryService.loadAvailableRepositories(accountId);
  }

  private _afterSave(response: CreateRepositoryResponse): void {
    // Stay on the add view and present the outcome checklist; Done returns to the list.
    this._addOutcome.set(response);
    this._reloadAll();
  }

  private _returnToList(): void {
    this._reorderRequested.set(false);
    this._addOutcome.set(null);
    this._repositoryView.set({ kind: 'list' });
    this._focusAfterRender(() => this._sectionHeading?.nativeElement);
  }

  private _focusAfterRender(target: () => HTMLElement | undefined): void {
    runInInjectionContext(this._injector, () => {
      afterNextRender(() => {
        target()?.focus();
      });
    });
  }

  private _reloadAll(): void {
    const accountIds = this.accountService.accounts().map(a => a.id);
    this._lastLoadedAccountIdsKey.set(this._accountIdsKey());
    this.repositoryService.loadAllRepositories(accountIds);
  }
}
