import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, WritableSignal, effect, inject, input, output, signal } from '@angular/core';
import { RepositoryService } from '../repository.service';
import { RecheckClaimsResponse, UnclaimedRepository } from '../repository.model';

export interface ClaimsRechecked {
  /** The row whose button was pressed; the page keys its focus rule on it. */
  readonly repositoryId: string;
  readonly host: string;
  readonly reclaimedRepositoryCount: number;
}

@Component({
  selector: 'fd-unclaimed-repository-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loadError(); as error) {
      <div class="unclaimed-repository-list__error" role="alert">
        <span class="unclaimed-repository-list__error-message">{{ error }}</span>
        <button
          class="unclaimed-repository-list__retry-btn"
          type="button"
          (click)="retry.emit()"
        >Retry</button>
      </div>
    }

    @if (repositories().length > 0) {
      <ul
        class="unclaimed-repository-list__list"
        aria-labelledby="unclaimed-repositories-heading"
        [attr.aria-busy]="reloading() || null"
      >
        @for (repository of repositories(); track repository.id) {
          <li class="unclaimed-repository-list__row">
            <div class="unclaimed-repository-list__main">
              <span class="unclaimed-repository-list__slug">{{ repository.slug }}</span>
              <p class="unclaimed-repository-list__meta">{{ repository.host }}</p>
            </div>

            <div class="unclaimed-repository-list__side">
              <button
                type="button"
                class="unclaimed-repository-list__icon-btn unclaimed-repository-list__delete-btn"
                [disabled]="reloading() || removingId() === repository.id"
                [attr.aria-label]="'Delete repository ' + repository.slug"
                [attr.title]="'Delete repository ' + repository.slug"
                (click)="delete.emit(repository)"
              ><svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6" /><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" /><path d="M10 11v6" /><path d="M14 11v6" /><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" /></svg></button>
            </div>

            <div class="unclaimed-repository-list__problem">
              <p class="unclaimed-repository-list__problem-text">{{ repository.violationDescription }}</p>
              @if (_recheckError()?.id === repository.id) {
                <p class="unclaimed-repository-list__problem-error" role="alert">{{ _recheckError()!.message }}</p>
              }
              <div class="unclaimed-repository-list__problem-actions">
                <button
                  type="button"
                  class="unclaimed-repository-list__recheck-btn"
                  [disabled]="_recheckingHost() !== null || reloading() || removingId() === repository.id"
                  (click)="onRecheck(repository)"
                >{{ _recheckingId() === repository.id ? 'Re-checking…' : 'Re-check claims' }}</button>
              </div>
            </div>
          </li>
        }
      </ul>
    }
  `,
  styleUrl: './unclaimed-repository-list.scss',
})
export class UnclaimedRepositoryListComponent {
  private readonly _repositoryService = inject(RepositoryService);

  readonly repositories: InputSignal<readonly UnclaimedRepository[]> = input.required<readonly UnclaimedRepository[]>();
  readonly loadError: InputSignal<string | null> = input<string | null>(null);
  readonly reloading: InputSignal<boolean> = input(false);
  readonly removingId: InputSignal<string | null> = input<string | null>(null);

  readonly delete: OutputEmitterRef<UnclaimedRepository> = output<UnclaimedRepository>();
  readonly retry: OutputEmitterRef<void> = output<void>();
  readonly recheckStarted: OutputEmitterRef<void> = output<void>();
  readonly rechecked: OutputEmitterRef<ClaimsRechecked> = output<ClaimsRechecked>();

  // The host is what the re-check acts on; the id marks which row's button was pressed.
  protected readonly _recheckingHost: WritableSignal<string | null> = signal(null);
  protected readonly _recheckingId: WritableSignal<string | null> = signal(null);
  protected readonly _recheckError: WritableSignal<{ id: string; message: string } | null> = signal(null);

  constructor() {
    effect(() => {
      if (this.removingId() !== null) {
        this._recheckError.set(null);
      }
    });
  }

  onRecheck(repository: UnclaimedRepository): void {
    if (this._recheckingHost() !== null) {
      return;
    }
    this._recheckError.set(null);
    this._recheckingHost.set(repository.host);
    this._recheckingId.set(repository.id);
    this.recheckStarted.emit();

    this._repositoryService.recheckClaims(repository.host).subscribe({
      next: (response: RecheckClaimsResponse) => {
        this._recheckingHost.set(null);
        this._recheckingId.set(null);
        this.rechecked.emit({ repositoryId: repository.id, host: repository.host, reclaimedRepositoryCount: response.reclaimedRepositoryCount });
      },
      error: () => {
        this._recheckingHost.set(null);
        this._recheckingId.set(null);
        this._recheckError.set({ id: repository.id, message: 'Re-check failed. Please try again.' });
      },
    });
  }
}
