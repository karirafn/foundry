import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, WritableSignal, inject, input, output, signal } from '@angular/core';
import { RepositorySummary, eligibilityStatusLabel } from '../repository.model';
import { RepositoryEligibilityComponent } from '../repository-eligibility/repository-eligibility';
import { RepositoryEligibilityDetailsComponent } from '../repository-eligibility-details/repository-eligibility-details';
import { RepositoryService } from '../repository.service';
import { ProviderIconComponent } from '../../../../shared/components/provider-icon/provider-icon';

@Component({
  selector: 'fd-repository-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RepositoryEligibilityComponent, RepositoryEligibilityDetailsComponent, ProviderIconComponent],
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
      <ul class="repository-list__list" role="list">
        @for (repo of repositories(); track repo.id) {
          <li class="repository-list__item" role="listitem">
            <div class="repository-list__slug-row">
              <fd-provider-icon
                [providerType]="repo.providerType"
                class="repository-list__provider"
              />
              <span class="repository-list__slug">{{ repo.slug }}</span>
              <div class="repository-list__slug-row-end">
                @if (repo.eligibility) {
                  <div class="repository-list__eligibility-group">
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
                  <button
                    class="repository-list__edit-btn"
                    type="button"
                    [attr.aria-label]="'Edit repository ' + repo.slug"
                    (click)="edit.emit(repo)"
                  >Edit</button>
                  <button
                    class="repository-list__delete-btn"
                    type="button"
                    [attr.aria-label]="'Delete repository ' + repo.slug"
                    (click)="delete.emit(repo)"
                  >Delete</button>
                </div>
              </div>
            </div>
            <div class="repository-list__meta-row">
              <span class="repository-list__account-name" [title]="repo.accountName">{{ repo.accountName }}</span>
              <span class="repository-list__poll-interval">
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
    return `${minutes} min`;
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
