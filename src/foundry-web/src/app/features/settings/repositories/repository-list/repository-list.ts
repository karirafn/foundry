import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, WritableSignal, computed, inject, input, output, signal } from '@angular/core';
import { CdkDragDrop, CdkDropList, CdkDrag, CdkDragHandle } from '@angular/cdk/drag-drop';
import { RepositorySummary, eligibilityStatusLabel } from '../repository.model';
import { RepositoryEligibilityComponent } from '../repository-eligibility/repository-eligibility';
import { RepositoryEligibilityDetailsComponent } from '../repository-eligibility-details/repository-eligibility-details';
import { RepositoryService } from '../repository.service';
import { ProviderIconComponent } from '../../../../shared/components/provider-icon/provider-icon';
import { RowActionsComponent } from '../../../../shared/components/row-actions/row-actions';

@Component({
  selector: 'fd-repository-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RepositoryEligibilityComponent,
    RepositoryEligibilityDetailsComponent,
    ProviderIconComponent,
    RowActionsComponent,
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
        <span class="repository-list__loading-spinner" aria-hidden="true"></span>
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
            role="listitem"
            cdkDrag
            [id]="'repo-item-' + repo.id"
            [attr.aria-roledescription]="_multipleRepos() ? 'reorderable item' : null"
          >
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
            <div class="repository-list__identity">
              <fd-provider-icon
                [providerType]="repo.providerType"
                class="repository-list__provider"
              />
              <span
                class="repository-list__slug"
                [title]="repo.slug + ' — ' + repo.accountName"
              >{{ repo.slug }}</span>
            </div>
            <div class="repository-list__metadata">
              <span
                class="repository-list__poll-interval"
                [title]="pollIntervalTitle(repo.pollIntervalSeconds)"
              >
                {{ pollIntervalLabel(repo.pollIntervalSeconds) }}
              </span>
              <div class="repository-list__status">
                <span
                  class="repository-list__status-dot repository-list__status-dot--{{ repo.isActive ? 'active' : 'paused' }}"
                  aria-hidden="true"
                ></span>
                <span class="repository-list__status-label">
                  {{ repo.isActive ? 'Active' : 'Paused' }}
                </span>
              </div>
              <span class="repository-list__last-polled">
                {{ lastPolledLabel(repo.lastPolledAt) }}
              </span>
            </div>
            @if (repo.eligibility) {
              <div class="repository-list__eligibility-group">
                <span class="sr-only">{{ eligibilityStatusLabel(repo.eligibility.status) }}</span>
                <fd-repository-eligibility
                  class="repository-list__eligibility"
                  [status]="repo.eligibility.status"
                  [recheckPending]="_recheckingId() === repo.id"
                />
                @if (repo.eligibility.status !== 'eligible') {
                  <button
                    class="repository-list__toggle-btn"
                    type="button"
                    [attr.aria-expanded]="_expandedId() === repo.id ? 'true' : 'false'"
                    [attr.aria-controls]="'eligibility-detail-' + repo.id"
                    [attr.aria-label]="'Branch protection details for ' + repo.slug"
                    (click)="toggleExpand(repo.id)"
                    (keydown.enter)="onToggleKeydown($event, repo.id)"
                    (keydown.space)="onToggleKeydown($event, repo.id)"
                  >
                    <svg
                      class="repository-list__toggle-chevron"
                      [class.repository-list__toggle-chevron--expanded]="_expandedId() === repo.id"
                      xmlns="http://www.w3.org/2000/svg"
                      width="12"
                      height="12"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      stroke-width="2.5"
                      stroke-linecap="round"
                      stroke-linejoin="round"
                      aria-hidden="true"
                    >
                      <polyline points="6 9 12 15 18 9" />
                    </svg>
                  </button>
                }
              </div>
            }
            <div class="repository-list__actions">
              <fd-row-actions
                [editLabel]="'Edit repository ' + repo.slug"
                [deleteLabel]="'Delete repository ' + repo.slug"
                (edit)="edit.emit(repo)"
                (delete)="delete.emit(repo)"
              />
            </div>
            @if (repo.eligibility && repo.eligibility.status !== 'eligible') {
              <fd-repository-eligibility-details
                [hidden]="_expandedId() !== repo.id"
                [id]="'eligibility-detail-' + repo.id"
                [panelId]="'eligibility-detail-' + repo.id"
                [status]="repo.eligibility.status"
                [violations]="repo.eligibility.violations"
                [recheckPending]="_recheckingId() === repo.id"
                [recheckError]="_recheckError()?.id === repo.id ? _recheckError()!.message : null"
                (recheck)="onRecheck(repo)"
              />
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
  readonly edit: OutputEmitterRef<RepositorySummary> = output<RepositorySummary>();
  readonly delete: OutputEmitterRef<RepositorySummary> = output<RepositorySummary>();
  readonly retry: OutputEmitterRef<void> = output<void>();

  protected readonly _expandedId: WritableSignal<string | null> = signal(null);
  protected readonly _recheckingId: WritableSignal<string | null> = signal(null);
  protected readonly _recheckError: WritableSignal<{ id: string; message: string } | null> = signal(null);
  protected readonly _announcement: WritableSignal<string> = signal('');
  protected readonly _moveError: WritableSignal<string | null> = signal(null);
  protected readonly _multipleRepos = computed(() => this.repositories().length > 1);

  readonly eligibilityStatusLabel = eligibilityStatusLabel;

  toggleExpand(id: string): void {
    if (this._expandedId() === id) {
      this._expandedId.set(null);
    } else {
      this._expandedId.set(id);
    }
  }

  onToggleKeydown(event: Event, id: string): void {
    event.preventDefault();
    this.toggleExpand(id);
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
        const wasExpanded = this._expandedId() === repo.id;
        this._recheckingId.set(null);
        if (wasExpanded) {
          this._expandedId.set(null);
        }
        const status = updated.eligibility?.status;
        this._announcement.set(status ? `${repo.slug}: ${eligibilityStatusLabel(status)}` : '');
      },
      error: () => {
        this._recheckingId.set(null);
        this._recheckError.set({ id: repo.id, message: 'Re-check failed. Please try again.' });
        this._announcement.set(`${repo.slug}: Re-check failed`);
      },
    });
  }

  pollIntervalLabel(pollIntervalSeconds: number | null): string {
    if (pollIntervalSeconds === null) {
      return '—';
    }
    const minutes = Math.round(pollIntervalSeconds / 60);
    return `${minutes}m`;
  }

  pollIntervalTitle(pollIntervalSeconds: number | null): string {
    if (pollIntervalSeconds === null) {
      return 'Poll interval not set';
    }
    const minutes = Math.round(pollIntervalSeconds / 60);
    return `Polls every ${minutes} minute${minutes === 1 ? '' : 's'}`;
  }

  lastPolledLabel(lastPolledAt: string | null): string {
    if (lastPolledAt === null) {
      return 'Never';
    }
    const diff = Date.now() - new Date(lastPolledAt).getTime();
    const minutes = Math.floor(diff / 60_000);
    if (minutes < 1) {
      return 'Just now';
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
}
