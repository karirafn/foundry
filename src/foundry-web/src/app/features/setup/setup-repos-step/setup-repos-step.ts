import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  OutputEmitterRef,
  Signal,
  WritableSignal,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { from } from 'rxjs';
import { concatMap } from 'rxjs/operators';
import { RepositoryService } from '../../settings/repositories/repository.service';
import { AvailableRepository } from '../../settings/repositories/repository.model';

const ERROR_TRUNCATE_LENGTH = 200;
const NO_WRITE_ACCESS_REASON = 'no write access — token lacks push or SSO not authorized';

@Component({
  selector: 'fd-setup-repos-step',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="setup-repos-step">
      <h2 class="setup-repos-step__title">Select Repositories</h2>
      <p class="setup-repos-step__description">
        Choose which repositories Foundry should monitor for issues.
      </p>

      @if (_repositoryService.loadingAvailable()) {
        <div class="setup-repos-step__loading" role="status">
          <span class="setup-repos-step__loading-spinner" aria-hidden="true"></span>
          Loading repositories...
        </div>
      } @else if (_repositoryService.loadAvailableError()) {
        <div class="setup-repos-step__load-error" role="alert">
          {{ _repositoryService.loadAvailableError() }}
          <button
            class="setup-repos-step__retry-btn"
            type="button"
            (click)="onRetry()"
          >Retry</button>
        </div>
      } @else {
        <div class="setup-repos-step__filter-wrapper">
          <input
            class="setup-repos-step__filter-input"
            type="text"
            placeholder="Filter repositories..."
            aria-label="Filter repositories"
            [value]="_filterText()"
            (input)="_filterText.set($any($event.target).value)"
          />
        </div>

        <ul class="setup-repos-step__repo-list" role="list">
          @if (_filteredRepositories().length === 0) {
            <li class="setup-repos-step__repo-empty">No matching repositories</li>
          }
          @for (repo of _filteredRepositories(); track repo.slug) {
            <li
              class="setup-repos-step__repo-item"
              [class.setup-repos-step__repo-item--disabled]="!repo.canPush"
            >
              <label class="setup-repos-step__repo-label">
                <input
                  class="setup-repos-step__repo-checkbox"
                  type="checkbox"
                  [checked]="_selectedSlugs().has(repo.slug) && repo.canPush"
                  [disabled]="!repo.canPush"
                  [attr.aria-describedby]="repo.canPush ? null : 'repo-reason-' + repo.slug"
                  (change)="onToggle(repo.slug, $any($event.target).checked)"
                />
                <span class="setup-repos-step__repo-slug">{{ repo.slug }}</span>
                @if (repo.isPrivate) {
                  <span class="setup-repos-step__repo-private-badge" aria-label="private">Private</span>
                }
                @if (!repo.canPush) {
                  <span
                    class="setup-repos-step__repo-reason"
                    [id]="'repo-reason-' + repo.slug"
                  >{{ _noWriteAccessReason }}</span>
                }
              </label>
            </li>
          }
        </ul>
      }

      <div class="setup-repos-step__save-error" role="alert">{{ _saveError() ?? '' }}</div>

      @if (_saving()) {
        <div class="setup-repos-step__saving-indicator" role="status" aria-live="polite">
          Creating repositories...
        </div>
      }

      <div class="setup-repos-step__actions">
        <button
          class="setup-repos-step__back-btn"
          type="button"
          (click)="back.emit()"
        >Back</button>

        <div class="setup-repos-step__secondary-actions">
          <button
            class="setup-repos-step__skip-btn"
            type="button"
            (click)="onSkip()"
          >Skip</button>

          <button
            class="setup-repos-step__finish-btn"
            type="button"
            [disabled]="!_canFinish()"
            (click)="onFinish()"
          >{{ _saving() ? 'Creating...' : 'Finish' }}</button>
        </div>
      </div>
    </div>
  `,
  styleUrl: './setup-repos-step.scss',
})
export class SetupReposStepComponent implements OnInit {
  protected readonly _noWriteAccessReason = NO_WRITE_ACCESS_REASON;
  protected readonly _repositoryService = inject(RepositoryService);

  readonly accountId = input.required<string>();

  readonly complete: OutputEmitterRef<void> = output<void>();
  readonly back: OutputEmitterRef<void> = output<void>();

  protected readonly _filterText: WritableSignal<string> = signal('');
  protected readonly _selectedSlugs: WritableSignal<Set<string>> = signal(new Set<string>());
  protected readonly _saving: WritableSignal<boolean> = signal(false);
  protected readonly _saveError: WritableSignal<string | null> = signal(null);

  protected readonly _filteredRepositories: Signal<AvailableRepository[]> = computed(() => {
    const filter = this._filterText().toLowerCase();
    const repos = this._repositoryService.availableRepositories();
    if (!filter) {
      return repos;
    }
    return repos.filter(r => r.slug.toLowerCase().includes(filter));
  });

  protected readonly _canFinish: Signal<boolean> = computed(() => {
    if (this._saving()) {
      return false;
    }
    return this._selectedSlugs().size > 0;
  });

  ngOnInit(): void {
    this._repositoryService.loadAvailableRepositories(this.accountId());
  }

  onToggle(slug: string, checked: boolean): void {
    this._selectedSlugs.update(current => {
      const next = new Set(current);
      if (checked) {
        next.add(slug);
      } else {
        next.delete(slug);
      }
      return next;
    });
  }

  onRetry(): void {
    this._repositoryService.loadAvailableRepositories(this.accountId());
  }

  onSkip(): void {
    this.complete.emit();
  }

  onFinish(): void {
    const accountId = this.accountId();
    const slugs = Array.from(this._selectedSlugs());
    const total = slugs.length;
    let successCount = 0;

    this._saving.set(true);
    this._saveError.set(null);

    from(slugs).pipe(
      concatMap(slug => this._repositoryService.createRepository(accountId, { slug, pollIntervalSeconds: null })),
    ).subscribe({
      next: () => {
        successCount++;
      },
      complete: () => {
        this._saving.set(false);
        this.complete.emit();
      },
      error: (err: HttpErrorResponse) => {
        this._saving.set(false);
        const rawMessage = typeof err.error === 'string' && err.error
          ? err.error.slice(0, ERROR_TRUNCATE_LENGTH)
          : (err.message ?? 'Failed to create repositories').slice(0, ERROR_TRUNCATE_LENGTH);
        this._saveError.set(`Created ${successCount} of ${total} repositories. Error: ${rawMessage}`);
      },
    });
  }
}
