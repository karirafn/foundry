import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, WritableSignal, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CdkDragDrop, CdkDropList, CdkDrag, CdkDragHandle } from '@angular/cdk/drag-drop';
import { RepositorySummary, EligibilityStatus, eligibilityStatusLabel } from '../repository.model';
import { RepositoryEligibilityComponent } from '../repository-eligibility/repository-eligibility';
import { RepositoryService } from '../repository.service';
import { ProviderIconComponent } from '../../../../shared/components/provider-icon/provider-icon';
import { DeleteButtonComponent } from '../../../../shared/components/delete-button/delete-button';
import { SpinnerComponent } from '../../../../shared/components/spinner/spinner';
import { TooltipDirective } from '../../../../shared/directives/tooltip/tooltip.directive';
import { unreachableExplanation, rateLimitTooltip } from '../repository-eligibility-unreachable.util';

@Component({
  selector: 'fd-repository-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    RepositoryEligibilityComponent,
    ProviderIconComponent,
    DeleteButtonComponent,
    SpinnerComponent,
    TooltipDirective,
    CdkDropList,
    CdkDrag,
    CdkDragHandle,
  ],
  template: `
    <span
      class="sr-only repository-list__announcement"
      aria-live="polite"
      aria-atomic="true"
    >{{ _announcement() }}</span>

    @if (error()) {
      <div class="repository-list__error" role="alert">
        <span class="repository-list__error-message">{{ error() }}</span>
        <button
          class="repository-list__retry-btn"
          type="button"
          (click)="retry.emit()"
        >Retry</button>
      </div>
    }

    @if (loading()) {
      <div class="repository-list__loading" role="status" aria-label="Loading repositories">
        <fd-spinner [size]="24" />
        <span class="sr-only">Loading repositories</span>
      </div>
    }

    @if (!loading() && !error() && repositories().length === 0) {
      <div class="repository-list__empty">
        <p class="repository-list__empty-heading">No repositories monitored</p>
        <p class="repository-list__empty-description">
          Add your first repository to start monitoring for issues.
        </p>
        <button
          class="repository-list__add-btn"
          type="button"
          (click)="add.emit()"
        >+ Add Repository</button>
      </div>
    }

    @if (!loading() && !error() && repositories().length > 0) {
      <div class="repository-list__header">
        <span></span>
        <button
          class="repository-list__add-btn"
          type="button"
          (click)="add.emit()"
        >+ Add Repository</button>
      </div>

      @if (_moveError()) {
        <div class="repository-list__move-error" role="alert">
          {{ _moveError() }}
        </div>
      }

      @if (_multipleRepos()) {
        <p id="repository-priority-hint" class="repository-list__priority-hint">
          Issues are claimed in this priority order — the repository at the top is dispatched first. Drag or use the arrow buttons to reorder.
        </p>
      }

      <ul
        class="repository-list__list"
        role="list"
        aria-label="Repository priority order"
        [attr.aria-describedby]="_multipleRepos() ? 'repository-priority-hint' : null"
        cdkDropList
        (cdkDropListDropped)="onDrop($event)"
      >
        @for (repo of repositories(); track repo.id; let i = $index) {
          <li
            class="repository-list__item"
            [class.repository-list__item--paused]="!repo.isActive"
            role="listitem"
            cdkDrag
            [id]="'repo-item-' + repo.id"
            [attr.aria-roledescription]="_multipleRepos() ? 'reorderable item' : null"
          >
            <!-- TIER 1: reorder + provider icon + slug anchor + delete -->
            <div class="repository-list__line1">
              @if (_multipleRepos()) {
                <div class="repository-list__reorder-group">
                  <button
                    class="repository-list__drag-handle"
                    cdkDragHandle
                    type="button"
                    [attr.aria-label]="'Reorder ' + repo.slug + ', use arrow keys to move'"
                    (keydown.arrowup)="onMoveKey($event, i, -1)"
                    (keydown.arrowdown)="onMoveKey($event, i, 1)"
                    (keydown.home)="onMoveKey($event, i, -i)"
                    (keydown.end)="onMoveKey($event, i, repositories().length - 1 - i)"
                  >
                    <svg
                      aria-hidden="true"
                      xmlns="http://www.w3.org/2000/svg"
                      width="16"
                      height="16"
                      viewBox="0 0 24 24"
                      fill="currentColor"
                    >
                      <circle cx="9" cy="6" r="1.5" />
                      <circle cx="15" cy="6" r="1.5" />
                      <circle cx="9" cy="12" r="1.5" />
                      <circle cx="15" cy="12" r="1.5" />
                      <circle cx="9" cy="18" r="1.5" />
                      <circle cx="15" cy="18" r="1.5" />
                    </svg>
                  </button>
                  <div class="repository-list__move-stack">
                    <button
                      class="repository-list__move-up-btn"
                      type="button"
                      [attr.disabled]="i === 0 ? '' : null"
                      [attr.aria-label]="'Move ' + repo.slug + ' up'"
                      (click)="onMove(i, -1)"
                    >&#9650;</button>
                    <button
                      class="repository-list__move-down-btn"
                      type="button"
                      [attr.disabled]="i === repositories().length - 1 ? '' : null"
                      [attr.aria-label]="'Move ' + repo.slug + ' down'"
                      (click)="onMove(i, 1)"
                    >&#9660;</button>
                  </div>
                </div>
              }

              <fd-provider-icon
                [providerType]="repo.providerType"
                class="repository-list__provider"
              />

              <a
                class="repository-list__slug"
                [routerLink]="['/settings/repositories', repo.id]"
              >@for (segment of repo.slug.split('/'); track $index; let last = $last) {
                {{ segment }}@if (!last) {/<wbr>}
              }</a>

              <fd-delete-button
                class="repository-list__delete"
                [deleteLabel]="'Delete repository ' + repo.slug"
                (delete)="delete.emit(repo)"
              />
            </div>

            <!-- TIER 2: metadata strip -->
            <div
              class="repository-list__strip"
              role="group"
              [attr.aria-label]="'Status for ' + repo.slug"
            >
              <!-- Polling/Paused state word -->
              <div class="repository-list__state">
                <span
                  class="repository-list__state-dot repository-list__state-dot--{{ repo.isActive ? 'active' : 'paused' }}"
                  aria-hidden="true"
                ></span>
                <span class="repository-list__state-label">
                  {{ repo.isActive ? 'Polling' : 'Paused' }}
                </span>
              </div>

              <!-- Interval chip -->
              <span
                class="repository-list__chip repository-list__chip--interval"
                tabindex="0"
                [attr.aria-label]="intervalTooltip(repo)"
                [fdTooltip]="intervalTooltip(repo)"
              >
                <svg aria-hidden="true" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <circle cx="12" cy="12" r="10" />
                  <polyline points="12 6 12 12 16 14" />
                </svg>
                {{ intervalChipText(repo) }}@if (repo.pollIntervalIsDefault) {
                  <span class="repository-list__chip-default">default</span>
                }
              </span>

              <!-- Last-polled chip -->
              <span
                class="repository-list__chip repository-list__chip--last-polled"
                tabindex="0"
                [attr.aria-label]="lastPolledTooltip(repo)"
                [fdTooltip]="lastPolledTooltip(repo)"
              >
                <svg aria-hidden="true" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <polyline points="1 4 1 10 7 10" />
                  <path d="M3.51 15a9 9 0 1 0 .49-3" />
                </svg>
                {{ lastPolledChipText(repo) }}
              </span>

              <!-- Max-workers chip -->
              <span
                class="repository-list__chip repository-list__chip--max-workers"
                tabindex="0"
                [attr.aria-label]="maxWorkersTooltip(repo)"
                [fdTooltip]="maxWorkersTooltip(repo)"
              >
                <svg aria-hidden="true" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
                  <circle cx="9" cy="7" r="4" />
                  <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
                  <path d="M16 3.13a4 4 0 0 1 0 7.75" />
                </svg>
                {{ repo.maxConcurrentWorkers }}
              </span>

              <!-- Via account name -->
              <span class="repository-list__via">via {{ repo.accountName }}</span>

              <!-- Eligibility badge -->
              @if (repo.eligibility) {
                <fd-repository-eligibility
                  class="repository-list__eligibility"
                  [status]="repo.eligibility.status"
                  [recheckPending]="_recheckingId() === repo.id"
                />
              }
            </div>

            <!-- INLINE ELIGIBILITY REASONS (below strip) -->
            @if (repo.eligibility && repo.eligibility.status !== 'eligible') {
              <div
                class="repository-list__reasons"
                role="group"
                [attr.aria-label]="'Eligibility for ' + repo.slug"
              >
                @if (repo.eligibility.status === 'ineligible') {
                  @for (violation of repo.eligibility.violations; track violation.rule) {
                    <div class="repository-list__reason">
                      <svg aria-hidden="true" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
                        <line x1="12" y1="9" x2="12" y2="13" />
                        <line x1="12" y1="17" x2="12.01" y2="17" />
                      </svg>
                      {{ violation.description }}
                    </div>
                  }
                }

                @if (repo.eligibility.status === 'unreachable') {
                  <div class="repository-list__reason">
                    <svg aria-hidden="true" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                      <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
                      <line x1="12" y1="9" x2="12" y2="13" />
                      <line x1="12" y1="17" x2="12.01" y2="17" />
                    </svg>
                    {{ _unreachableExplanation(repo) }}
                  </div>
                }

                @if (_recheckError()?.id === repo.id) {
                  <div class="repository-list__reason--error" role="alert">
                    {{ _recheckError()!.message }}
                  </div>
                }

                <div class="repository-list__reason-actions">
                  <button
                    class="repository-list__recheck-btn"
                    type="button"
                    [disabled]="_recheckingId() !== null || _isRateLimited(repo)"
                    [fdTooltip]="_isRateLimited(repo) ? _rateLimitTooltip(repo) : null"
                    (click)="onRecheck(repo)"
                  >{{ _recheckingId() === repo.id ? 'Re-checking…' : 'Re-check' }}</button>
                </div>
              </div>
            }
          </li>
        }
      </ul>
    }
  `,
  styleUrl: './repository-list.scss',
})
export class RepositoryListComponent {
  private readonly _repositoryService = inject(RepositoryService);

  readonly repositories: InputSignal<RepositorySummary[]> = input<RepositorySummary[]>([]);
  readonly loading: InputSignal<boolean> = input<boolean>(false);
  readonly error: InputSignal<string | null> = input<string | null>(null);

  readonly add: OutputEmitterRef<void> = output<void>();
  readonly delete: OutputEmitterRef<RepositorySummary> = output<RepositorySummary>();
  readonly retry: OutputEmitterRef<void> = output<void>();

  protected readonly _recheckingId: WritableSignal<string | null> = signal(null);
  protected readonly _recheckError: WritableSignal<{ id: string; message: string } | null> = signal(null);
  protected readonly _announcement: WritableSignal<string> = signal('');
  protected readonly _moveError: WritableSignal<string | null> = signal(null);
  protected readonly _multipleRepos = computed(() => this.repositories().length > 1);

  readonly eligibilityStatusLabel = eligibilityStatusLabel;

  intervalChipText(repo: RepositorySummary): string {
    const seconds = repo.effectivePollIntervalSeconds;
    if (seconds < 60) {
      return `${seconds}s`;
    }
    return `${Math.round(seconds / 60)}m`;
  }

  intervalTooltip(repo: RepositorySummary): string {
    const seconds = repo.effectivePollIntervalSeconds;
    const minutes = Math.round(seconds / 60);
    const timeStr = seconds < 60
      ? `${seconds} second${seconds === 1 ? '' : 's'}`
      : `${minutes} minute${minutes === 1 ? '' : 's'}`;
    const base = `Polls every ${timeStr}`;
    return repo.pollIntervalIsDefault ? `${base} (default)` : base;
  }

  lastPolledChipText(repo: RepositorySummary): string {
    if (repo.lastPolledAt === null) {
      return 'never';
    }
    const diff = Date.now() - new Date(repo.lastPolledAt).getTime();
    const minutes = Math.floor(diff / 60_000);
    if (minutes < 1) {
      return 'just now';
    }
    if (minutes < 60) {
      return `${minutes}m ago`;
    }
    const hours = Math.floor(minutes / 60);
    if (hours < 24) {
      return `${hours}h ago`;
    }
    const days = Math.floor(hours / 24);
    return `${days}d ago`;
  }

  lastPolledTooltip(repo: RepositorySummary): string {
    if (repo.lastPolledAt === null) {
      return 'Never polled';
    }
    const diff = Date.now() - new Date(repo.lastPolledAt).getTime();
    const minutes = Math.floor(diff / 60_000);
    if (minutes < 1) {
      return 'Last polled just now';
    }
    if (minutes < 60) {
      return `Last polled ${minutes} minute${minutes === 1 ? '' : 's'} ago`;
    }
    const hours = Math.floor(minutes / 60);
    if (hours < 24) {
      return `Last polled ${hours} hour${hours === 1 ? '' : 's'} ago`;
    }
    const days = Math.floor(hours / 24);
    return `Last polled ${days} day${days === 1 ? '' : 's'} ago`;
  }

  maxWorkersTooltip(repo: RepositorySummary): string {
    return `Max ${repo.maxConcurrentWorkers} concurrent worker${repo.maxConcurrentWorkers === 1 ? '' : 's'}`;
  }

  protected _unreachableExplanation(repo: RepositorySummary): string {
    return unreachableExplanation(repo.eligibility?.reason ?? null, repo.providerType);
  }

  protected _isRateLimited(repo: RepositorySummary): boolean {
    return repo.eligibility?.reason === 'rate-limited';
  }

  protected _rateLimitTooltip(repo: RepositorySummary): string {
    return rateLimitTooltip(repo.providerType);
  }

  onDrop(event: CdkDragDrop<RepositorySummary[]>): void {
    if (event.previousIndex === event.currentIndex) {
      return;
    }
    const repo = this.repositories()[event.previousIndex];
    this._doMove(repo, event.currentIndex, () => {
      setTimeout(() => {
        const movedItem = document.getElementById(`repo-item-${repo.id}`);
        const focusTarget =
          movedItem?.querySelector<HTMLElement>('.repository-list__drag-handle') ??
          movedItem?.querySelector<HTMLElement>('.repository-list__move-up-btn') ??
          movedItem?.querySelector<HTMLElement>('.repository-list__move-down-btn');
        focusTarget?.focus();
      });
    });
  }

  onMove(currentIndex: number, delta: number): void {
    const newIndex = currentIndex + delta;
    if (newIndex < 0 || newIndex >= this.repositories().length) {
      return;
    }
    const repo = this.repositories()[currentIndex];
    const isMovingDown = delta > 0;
    this._doMove(repo, newIndex, () => {
      setTimeout(() => {
        const movedItem = document.getElementById(`repo-item-${repo.id}`);
        if (!movedItem) {
          return;
        }
        const totalRepos = this._repositoryService.repositories().length;
        const actualIndex = this._repositoryService.repositories().findIndex(r => r.id === repo.id);
        const isAtTop = actualIndex === 0;
        const isAtBottom = actualIndex === totalRepos - 1;

        if (isMovingDown && isAtBottom) {
          const focusTarget =
            movedItem.querySelector<HTMLElement>('.repository-list__move-up-btn') ??
            movedItem.querySelector<HTMLElement>('.repository-list__drag-handle');
          focusTarget?.focus();
        } else if (!isMovingDown && isAtTop) {
          const focusTarget =
            movedItem.querySelector<HTMLElement>('.repository-list__move-down-btn') ??
            movedItem.querySelector<HTMLElement>('.repository-list__drag-handle');
          focusTarget?.focus();
        } else {
          const btnClass = isMovingDown
            ? '.repository-list__move-down-btn'
            : '.repository-list__move-up-btn';
          const focusTarget =
            movedItem.querySelector<HTMLElement>(btnClass) ??
            movedItem.querySelector<HTMLElement>('.repository-list__drag-handle');
          focusTarget?.focus();
        }
      });
    });
  }

  onMoveKey(event: Event, currentIndex: number, delta: number): void {
    if (delta === 0) {
      return;
    }
    event.preventDefault();
    const newIndex = Math.max(0, Math.min(this.repositories().length - 1, currentIndex + delta));
    if (newIndex === currentIndex) {
      return;
    }
    const repo = this.repositories()[currentIndex];
    this._doMove(repo, newIndex, () => {
      const handle = document.getElementById(`repo-item-${repo.id}`)?.querySelector<HTMLElement>('.repository-list__drag-handle');
      handle?.focus();
    });
  }

  private _doMove(repo: RepositorySummary, newIndex: number, afterMove?: () => void): void {
    this._moveError.set(null);
    this._announcement.set('');
    this._repositoryService.moveRepository(repo.id, newIndex).subscribe({
      next: () => {
        const repos = this._repositoryService.repositories();
        const actualIndex = repos.findIndex(r => r.id === repo.id);
        const displayIndex = actualIndex !== -1 ? actualIndex + 1 : newIndex + 1;
        this._announcement.set(`${repo.slug} moved to position ${displayIndex}`);
        afterMove?.();
      },
      error: () => {
        this._moveError.set(`Failed to reorder ${repo.slug}. Please try again.`);
        this._announcement.set(`Failed to reorder ${repo.slug}`);
      },
    });
  }

  onRecheck(repo: RepositorySummary): void {
    if (this._recheckingId() !== null) {
      return;
    }
    this._recheckError.set(null);
    this._recheckingId.set(repo.id);
    this._announcement.set(`${repo.slug}: Re-checking...`);

    this._repositoryService.recheckEligibility(repo.accountId, repo.id).subscribe({
      next: (updated: RepositorySummary) => {
        this._recheckingId.set(null);
        const status = updated.eligibility?.status;
        const label = status ? eligibilityStatusLabel(status as EligibilityStatus) : '';
        this._announcement.set(status ? `${repo.slug}: ${label}` : '');
      },
      error: () => {
        this._recheckingId.set(null);
        this._recheckError.set({ id: repo.id, message: 'Re-check failed. Please try again.' });
        this._announcement.set(`${repo.slug}: Re-check failed`);
      },
    });
  }
}
