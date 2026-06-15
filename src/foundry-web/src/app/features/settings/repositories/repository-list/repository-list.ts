import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, input, output } from '@angular/core';
import { RepositorySummary } from '../repository.model';

@Component({
  selector: 'fd-repository-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
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
          </li>
        }
      </ul>
    }
  `,
  styleUrl: './repository-list.scss',
})
export class RepositoryListComponent {
  readonly repositories: InputSignal<RepositorySummary[]> = input<RepositorySummary[]>([]);
  readonly loading: InputSignal<boolean> = input<boolean>(false);
  readonly error: InputSignal<string | null> = input<string | null>(null);

  readonly add: OutputEmitterRef<void> = output<void>();
  readonly edit: OutputEmitterRef<RepositorySummary> = output<RepositorySummary>();
  readonly delete: OutputEmitterRef<RepositorySummary> = output<RepositorySummary>();
  readonly retry: OutputEmitterRef<void> = output<void>();

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
