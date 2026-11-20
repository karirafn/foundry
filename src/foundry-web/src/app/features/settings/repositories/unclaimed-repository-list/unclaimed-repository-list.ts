import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, input, output } from '@angular/core';
import { UnclaimedRepository } from '../repository.model';

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
            </div>
          </li>
        }
      </ul>
    }
  `,
  styleUrl: './unclaimed-repository-list.scss',
})
export class UnclaimedRepositoryListComponent {
  readonly repositories: InputSignal<readonly UnclaimedRepository[]> = input.required<readonly UnclaimedRepository[]>();
  readonly loadError: InputSignal<string | null> = input<string | null>(null);
  readonly reloading: InputSignal<boolean> = input(false);
  readonly removingId: InputSignal<string | null> = input<string | null>(null);

  readonly delete: OutputEmitterRef<UnclaimedRepository> = output<UnclaimedRepository>();
  readonly retry: OutputEmitterRef<void> = output<void>();
}
