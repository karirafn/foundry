import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  OnInit,
  Signal,
  ViewChild,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  runInInjectionContext,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { RepositoryService } from '../repository.service';
import { AccountService } from '../../accounts/account.service';
import { RepositoryFormComponent } from '../repository-form/repository-form';
import { RepositoryEligibilityDetailsComponent } from '../repository-eligibility-details/repository-eligibility-details';
import { SpinnerComponent } from '../../../../shared/components/spinner/spinner';
import { RepositorySummary, UpdateRepositoryRequest, CreateRepositoryRequest } from '../repository.model';

type ViewState = 'loading' | 'load-error' | 'not-found' | 'loaded';

@Component({
  selector: 'fd-repository-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    RepositoryFormComponent,
    RepositoryEligibilityDetailsComponent,
    SpinnerComponent,
  ],
  template: `
    <span class="sr-only" role="status" aria-live="polite" aria-atomic="true">
      {{ _statusAnnouncement() }}
    </span>

    <section class="repository-page__section">
      @switch (_viewState()) {
        @case ('loading') {
          <div class="repository-page__loading" role="status" aria-label="Loading repository">
            <fd-spinner [size]="24" />
            <span class="sr-only">Loading repository</span>
          </div>
        }
        @case ('load-error') {
          <div class="repository-page__load-error" role="alert">
            <p #loadErrorMessage class="repository-page__load-error-message" tabindex="-1">
              {{ _loadErrorText() }}
            </p>
            <div class="repository-page__load-error-actions">
              <button
                type="button"
                class="repository-page__retry-btn"
                (click)="onRetry()"
              >Retry</button>
              <a
                class="repository-page__back-link"
                routerLink="/settings/repositories"
              >Back to repositories</a>
            </div>
          </div>
        }
        @case ('not-found') {
          <div class="repository-page__not-found">
            <p class="repository-page__not-found-heading" #notFoundHeading tabindex="-1">Repository not found</p>
            <p class="repository-page__not-found-description">
              This repository is no longer monitored, or the link is out of date.
              It may have been removed from your settings.
            </p>
            <a
              class="repository-page__back-link repository-page__back-link--button"
              routerLink="/settings/repositories"
            >Back to repositories</a>
          </div>
        }
        @case ('loaded') {
          <h1
            id="repository-page-heading"
            class="repository-page__heading"
            tabindex="-1"
          >{{ _repository()!.slug }}</h1>
          <p class="repository-page__subtitle">{{ _repository()!.accountName }}</p>

          <fd-repository-form
            [repository]="_repository() ?? null"
            [accounts]="accountService.accounts()"
            [availableRepositories]="repositoryService.availableRepositories()"
            [hasClaims]="repositoryService.availableHasClaims()"
            [loadingAvailable]="repositoryService.loadingAvailable()"
            [loadAvailableError]="repositoryService.loadAvailableError()"
            [saving]="repositoryService.saving()"
            [saveError]="repositoryService.saveError()"
            (save)="onSave($event)"
            (cancel)="onBack()"
            (accountSelected)="onAccountSelected($event)"
          />

          @if (_repository()!.eligibility && _repository()!.eligibility!.status !== 'eligible') {
            <fd-repository-eligibility-details
              [panelId]="'repository-page-eligibility'"
              [status]="_repository()!.eligibility!.status"
              [violations]="_repository()!.eligibility!.violations"
              [reason]="_repository()!.eligibility!.reason"
              [recheckPending]="_recheckPending()"
              [recheckError]="_recheckError()"
              (recheck)="onRecheck()"
            />
          }
        }
      }
    </section>
  `,
  styleUrl: './repository-page.scss',
})
export class RepositoryPageComponent implements OnInit {
  protected readonly repositoryService = inject(RepositoryService);
  protected readonly accountService = inject(AccountService);
  private readonly _route = inject(ActivatedRoute);
  private readonly _router = inject(Router);
  private readonly _injector = inject(Injector);

  @ViewChild('notFoundHeading') private readonly _notFoundHeading?: ElementRef<HTMLElement>;
  @ViewChild('loadErrorMessage') private readonly _loadErrorMessage?: ElementRef<HTMLElement>;

  private readonly _repositoryId: Signal<string | null> = toSignal(
    this._route.paramMap.pipe(map(params => params.get('repositoryId'))),
    { initialValue: null }
  );

  // Tracks whether a load has been attempted — prevents not-found flash on cold/deep-link load.
  private readonly _loadAttempted: WritableSignal<boolean> = signal(false);

  // Mirrors the account-keyed dedup from settings-repositories to avoid re-triggering on unchanged accounts.
  private readonly _accountIdsKey: Signal<string> = computed(() =>
    this.accountService.accounts().map(a => a.id).sort().join(',')
  );
  private readonly _lastLoadedAccountIdsKey: WritableSignal<string> = signal('');

  protected readonly _repository: Signal<RepositorySummary | undefined> = computed(() => {
    const id = this._repositoryId();
    if (!id) {
      return undefined;
    }
    return this.repositoryService.repositories().find(r => r.id === id);
  });

  protected readonly _viewState: Signal<ViewState> = computed(() => {
    if (!this._loadAttempted() || this.repositoryService.loading()) {
      return 'loading';
    }
    if (this.repositoryService.loadError()) {
      return 'load-error';
    }
    if (this._repository() !== undefined) {
      return 'loaded';
    }
    return 'not-found';
  });

  protected readonly _loadErrorText: Signal<string> = computed(() =>
    this.repositoryService.loadError()
    ?? "Couldn't load repositories. Check your connection and try again."
  );

  protected readonly _statusAnnouncement: Signal<string> = computed(() => {
    switch (this._viewState()) {
      case 'loading':
        return 'Loading repository';
      case 'load-error':
        return 'Could not load repositories';
      case 'not-found':
        return 'Repository not found';
      case 'loaded':
        return '';
    }
  });

  private readonly _recheckPendingSignal: WritableSignal<boolean> = signal(false);
  private readonly _recheckErrorSignal: WritableSignal<string | null> = signal(null);

  protected readonly _recheckPending: Signal<boolean> = this._recheckPendingSignal.asReadonly();
  protected readonly _recheckError: Signal<string | null> = this._recheckErrorSignal.asReadonly();

  constructor() {
    // Account-keyed effect: triggers loadAllRepositories whenever the set of accounts changes.
    // Mirrors the pattern from settings-repositories.ts to avoid redundant loads.
    effect(() => {
      const key = this._accountIdsKey();
      if (key !== this._lastLoadedAccountIdsKey()) {
        this._lastLoadedAccountIdsKey.set(key);
        const accountIds = this.accountService.accounts().map(a => a.id);
        this._loadAttempted.set(true);
        this.repositoryService.loadAllRepositories(accountIds);
      }
    });

    effect(() => {
      const state = this._viewState();
      if (state === 'not-found' || state === 'load-error') {
        runInInjectionContext(this._injector, () => {
          afterNextRender(() => {
            if (state === 'not-found') {
              this._notFoundHeading?.nativeElement.focus();
            } else {
              this._loadErrorMessage?.nativeElement.focus();
            }
          });
        });
      }
    });
  }

  ngOnInit(): void {
    if (this.accountService.accounts().length === 0) {
      this.accountService.loadAccounts();
    }
  }

  onSave(request: CreateRepositoryRequest | UpdateRepositoryRequest): void {
    const repo = this._repository();
    if (!repo) {
      return;
    }
    this.repositoryService.updateRepository(repo.accountId, repo.id, request as UpdateRepositoryRequest)
      .subscribe({
        error: () => { /* handled via saveError signal */ },
      });
  }

  onBack(): void {
    this._router.navigate(['/settings/repositories']);
  }

  onRetry(): void {
    const accountIds = this.accountService.accounts().map(a => a.id);
    this.repositoryService.loadAllRepositories(accountIds);
  }

  onRecheck(): void {
    const repo = this._repository();
    if (!repo) {
      return;
    }
    this._recheckErrorSignal.set(null);
    this._recheckPendingSignal.set(true);
    this.repositoryService.recheckEligibility(repo.accountId, repo.id).subscribe({
      next: () => {
        this._recheckPendingSignal.set(false);
      },
      error: () => {
        this._recheckPendingSignal.set(false);
        this._recheckErrorSignal.set('Re-check failed. Please try again.');
      },
    });
  }

  onAccountSelected(_accountId: string): void {
    // No-op in edit mode — repository and account are fixed
  }
}
