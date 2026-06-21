import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, WritableSignal, inject, input, output, signal } from '@angular/core';
import { RepositorySummary } from '../repository.model';
import { RepositoryEligibilityComponent } from '../repository-eligibility/repository-eligibility';
import { RepositoryService } from '../repository.service';

@Component({
  selector: 'fd-repository-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RepositoryEligibilityComponent],
  template: `
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
            <span class="repository-list__account-badge" aria-hidden="true">
              {{ accountBadge(repo.accountName) }}
            </span>
            <div class="repository-list__info">
              <span class="repository-list__slug">{{ repo.slug }}</span>
              <span class="repository-list__account-name">{{ repo.accountName }}</span>
            </div>
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
            @if (repo.eligibility) {
              <fd-repository-eligibility
                class="repository-list__eligibility"
                [status]="repo.eligibility.status"
                [violations]="repo.eligibility.violations"
                [recheckPending]="_recheckingId() === repo.id"
              />
            }
            <div class="repository-list__actions">
              @if (repo.eligibility && repo.eligibility.status !== 'eligible') {
                <button
                  class="repository-list__recheck-btn"
                  [class.repository-list__recheck-btn--unreachable]="repo.eligibility.status === 'unreachable'"
                  type="button"
                  [disabled]="_recheckingId() === repo.id"
                  [attr.aria-label]="'Re-check eligibility for ' + repo.slug"
                  (click)="onRecheck(repo)"
                >{{ _recheckingId() === repo.id ? 'Re-checking...' : 'Re-check' }}</button>
                <span
                  class="repository-list__recheck-error"
                  role="alert"
                  [attr.aria-hidden]="_recheckError()?.id !== repo.id"
                >{{ _recheckError()?.id === repo.id ? _recheckError()!.message : '' }}</span>
              }
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

  protected readonly _recheckingId: WritableSignal<string | null> = signal(null);
  protected readonly _recheckError: WritableSignal<{ id: string; message: string } | null> = signal(null);

  onRecheck(repo: RepositorySummary): void {
    if (this._recheckingId() !== null) {
      return;
    }
    this._recheckError.set(null);
    this._recheckingId.set(repo.id);
    this._repositoryService.recheckEligibility(repo.accountId, repo.id).subscribe({
      next: () => { this._recheckingId.set(null); },
      error: () => {
        this._recheckingId.set(null);
        this._recheckError.set({ id: repo.id, message: 'Re-check failed. Please try again.' });
      },
    });
  }

  accountBadge(accountName: string): string {
    return accountName.slice(0, 2).toUpperCase();
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
