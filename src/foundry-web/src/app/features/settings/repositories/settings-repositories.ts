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
  afterNextRender,
  runInInjectionContext,
} from '@angular/core';
import { AccountService } from '../accounts/account.service';
import { RepositoryService } from './repository.service';
import { RepositoryListComponent } from './repository-list/repository-list';
import { RepositoryFormComponent } from './repository-form/repository-form';
import { RepositorySummary, CreateRepositoryRequest, UpdateRepositoryRequest } from './repository.model';

type RepositoryView = { kind: 'list' } | { kind: 'add' } | { kind: 'edit'; repository: RepositorySummary };

@Component({
  selector: 'fd-settings-repositories',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RepositoryListComponent, RepositoryFormComponent],
  template: `
    <div class="repositories-settings">
      <section class="repositories-settings__section">
        <h2 class="repositories-settings__section-title" #sectionHeading tabindex="-1">Repositories</h2>
        <p class="repositories-settings__section-description">
          Manage repositories to monitor for issues.
        </p>

        @switch (_repositoryView().kind) {
          @case ('list') {
            <fd-repository-list
              [repositories]="repositoryService.repositories()"
              [loading]="repositoryService.loading()"
              [error]="repositoryService.loadError()"
              (add)="onAdd()"
              (edit)="onEdit($event)"
              (delete)="onDelete($event)"
              (retry)="reloadRepositories()"
            />
            <div class="repositories-settings__delete-error" role="alert">
              @if (repositoryService.deleteError(); as deleteError) {
                {{ deleteError }}
              }
            </div>
          }
          @case ('add') {
            <fd-repository-form
              [accounts]="accountService.accounts()"
              [availableRepositories]="repositoryService.availableRepositories()"
              [loadingAvailable]="repositoryService.loadingAvailable()"
              [loadAvailableError]="repositoryService.loadAvailableError()"
              [saving]="repositoryService.saving()"
              [saveError]="repositoryService.saveError()"
              (save)="onSave($event)"
              (cancel)="onCancel()"
              (accountSelected)="onAccountSelected($event)"
            />
          }
          @case ('edit') {
            <fd-repository-form
              [repository]="_editRepository"
              [accounts]="accountService.accounts()"
              [availableRepositories]="repositoryService.availableRepositories()"
              [loadingAvailable]="repositoryService.loadingAvailable()"
              [loadAvailableError]="repositoryService.loadAvailableError()"
              [saving]="repositoryService.saving()"
              [saveError]="repositoryService.saveError()"
              (save)="onSave($event)"
              (cancel)="onCancel()"
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
  private readonly _selectedAccountId: WritableSignal<string> = signal('');
  private readonly _accountIdsKey: Signal<string> = computed(() =>
    this.accountService.accounts().map(a => a.id).sort().join(',')
  );
  private readonly _lastLoadedAccountIdsKey: WritableSignal<string> = signal('');

  protected get _editRepository(): RepositorySummary {
    return (this._repositoryView() as { kind: 'edit'; repository: RepositorySummary }).repository;
  }

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

  onAdd(): void {
    this._repositoryView.set({ kind: 'add' });
  }

  onEdit(repository: RepositorySummary): void {
    this._repositoryView.set({ kind: 'edit', repository });
  }

  onCancel(): void {
    this._repositoryView.set({ kind: 'list' });
    runInInjectionContext(this._injector, () => {
      afterNextRender(() => {
        this._sectionHeading?.nativeElement.focus();
      });
    });
  }

  onSave(request: CreateRepositoryRequest | UpdateRepositoryRequest): void {
    const view = this._repositoryView();
    if (view.kind === 'add') {
      this.repositoryService.createRepository(this._selectedAccountId(), request as CreateRepositoryRequest)
        .subscribe({
          next: () => this._afterSave(),
          error: () => { /* handled via saveError signal */ },
        });
    } else if (view.kind === 'edit') {
      const repo = view.repository;
      this.repositoryService.updateRepository(repo.accountId, repo.id, request as UpdateRepositoryRequest)
        .subscribe({
          next: () => this._afterSave(),
          error: () => { /* handled via saveError signal */ },
        });
    }
  }

  onDelete(repository: RepositorySummary): void {
    if (window.confirm(`Delete repository "${repository.slug}"?`)) {
      this.repositoryService.deleteRepository(repository.accountId, repository.id)
        .subscribe({
          next: () => this._reloadAll(),
          error: () => { /* handled via deleteError signal */ },
        });
    }
  }

  onAccountSelected(accountId: string): void {
    this._selectedAccountId.set(accountId);
    this.repositoryService.loadAvailableRepositories(accountId);
  }

  private _afterSave(): void {
    this._repositoryView.set({ kind: 'list' });
    this._reloadAll();
    runInInjectionContext(this._injector, () => {
      afterNextRender(() => {
        this._sectionHeading?.nativeElement.focus();
      });
    });
  }

  private _reloadAll(): void {
    const accountIds = this.accountService.accounts().map(a => a.id);
    this._lastLoadedAccountIdsKey.set(this._accountIdsKey());
    this.repositoryService.loadAllRepositories(accountIds);
  }
}
