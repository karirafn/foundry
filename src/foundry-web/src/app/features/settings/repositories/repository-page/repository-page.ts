import {
  ChangeDetectionStrategy,
  Component,
  Signal,
  computed,
  inject,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { RepositoryService } from '../repository.service';
import { AccountService } from '../../accounts/account.service';
import { RepositoryFormComponent } from '../repository-form/repository-form';
import { RepositoryEligibilityDetailsComponent } from '../repository-eligibility-details/repository-eligibility-details';
import { SpinnerComponent } from '../../../../shared/components/spinner/spinner';
import { RepositorySummary, UpdateRepositoryRequest, CreateRepositoryRequest } from '../repository.model';

type ViewState = 'loading' | 'not-found' | 'loaded';

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
        @case ('not-found') {
          <div class="repository-page__not-found">
            <p class="repository-page__not-found-heading">Repository not found</p>
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
export class RepositoryPageComponent {
  protected readonly repositoryService = inject(RepositoryService);
  protected readonly accountService = inject(AccountService);
  private readonly _route = inject(ActivatedRoute);

  private readonly _repositoryId: Signal<string | null> = toSignal(
    this._route.paramMap.pipe(map(params => params.get('repositoryId'))),
    { initialValue: null }
  );

  protected readonly _repository: Signal<RepositorySummary | undefined> = computed(() => {
    const id = this._repositoryId();
    if (!id) {
      return undefined;
    }
    return this.repositoryService.repositories().find(r => r.id === id);
  });

  protected readonly _viewState: Signal<ViewState> = computed(() => {
    if (this.repositoryService.loading()) {
      return 'loading';
    }
    if (this._repository() !== undefined) {
      return 'loaded';
    }
    return 'not-found';
  });

  protected readonly _statusAnnouncement: Signal<string> = computed(() => {
    switch (this._viewState()) {
      case 'loading':
        return 'Loading repository';
      case 'not-found':
        return 'Repository not found';
      case 'loaded':
        return '';
    }
  });

  protected readonly _recheckPending: Signal<boolean> = computed(() => false);
  protected readonly _recheckError: Signal<string | null> = computed(() => null);

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
    // Navigation handled via routerLink in templates for non-loaded states.
    // In loaded state, the form's Cancel button calls this.
    // Router injection deferred to later step when navigation is wired end-to-end.
  }

  onRecheck(): void {
    const repo = this._repository();
    if (!repo) {
      return;
    }
    this.repositoryService.recheckEligibility(repo.accountId, repo.id).subscribe({
      error: () => { /* no-op — service handles errors */ },
    });
  }

  onAccountSelected(_accountId: string): void {
    // No-op in edit mode — repository and account are fixed
  }
}
