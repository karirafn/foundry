import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  computed,
  input,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { RepoAlert } from '../../board.model';

/** A repository that holds queued issues because it can't be worked on, with a way to fix it. */
@Component({
  selector: 'fd-repo-alert',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="repo-alert">
      <svg
        class="repo-alert__icon"
        xmlns="http://www.w3.org/2000/svg"
        width="16"
        height="16"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
        focusable="false"
      >
        <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3" />
        <path d="M12 9v4M12 17h.01" />
      </svg>
      <div class="repo-alert__body">
        <p class="repo-alert__lead">
          <strong class="repo-alert__repo">{{ alert().slug }}</strong> can't be worked on ·
          {{ heldText() }}
        </p>
        @if (violationText(); as text) {
          <p class="repo-alert__violation">{{ text }}</p>
        }
      </div>
      <a class="repo-alert__link" [routerLink]="['/settings/repositories', alert().repositoryId]"
        >Fix in repository settings</a
      >
    </div>
  `,
  styleUrl: './repo-alert.scss',
})
export class RepoAlertComponent {
  readonly alert: InputSignal<RepoAlert> = input.required<RepoAlert>();

  protected readonly heldText: Signal<string> = computed(() => {
    const count = this.alert().heldCount;
    return count === 1 ? '1 issue held' : `${count} issues held`;
  });

  protected readonly violationText: Signal<string | null> = computed(() => {
    const { firstViolation, moreViolations } = this.alert();
    if (firstViolation === null) {
      return null;
    }
    return moreViolations > 0 ? `${firstViolation} and ${moreViolations} more` : firstViolation;
  });
}
