import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, computed, input, output } from '@angular/core';
import { AffectedRepository, AffectedRepositoryStatus, affectedStatusLabel } from '../account.model';

const PANEL_HEADING_ID = 'affected-repos-heading';
const CREDENTIAL_UNREADABLE_REASON = "Skipped — the account token can't be decrypted. Re-enter it on the account to resume.";

function isLostAccess(status: AffectedRepositoryStatus): boolean {
  return status === 'ineligible' || status === 'unreachable' || status === 'credential-unreadable';
}

@Component({
  selector: 'fd-affected-repositories',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="affected-repositories"
      [class.affected-repositories--warning]="_hasLostAccess()"
      role="region"
      [attr.aria-labelledby]="headingId"
    >
      <div class="affected-repositories__header">
        <h3
          class="affected-repositories__heading"
          [id]="headingId"
        >
          @if (_hasLostAccess()) {
            {{ _lostAccessCount() }} repositories may have lost or restricted access with the new token
          } @else {
            {{ repositories().length }} repositories changed eligibility
          }
        </h3>
        <button
          class="affected-repositories__dismiss"
          type="button"
          (click)="dismiss.emit()"
        >Dismiss</button>
      </div>

      <ul class="affected-repositories__list" aria-label="Affected repositories">
        @for (repo of _sortedRepositories(); track repo.id) {
          @let reason = reasonFor(repo.newStatus);
          <li
            class="affected-repositories__row"
            [attr.aria-label]="repo.slug + ': was ' + _labelFor(repo.previousStatus) + ', now ' + _labelFor(repo.newStatus) + (reason ? '. ' + reason : '')"
          >
            <span class="affected-repositories__slug">{{ repo.slug }}</span>
            <span class="affected-repositories__transition">
              was {{ _labelFor(repo.previousStatus) }}
              <span class="affected-repositories__arrow" aria-hidden="true">→</span>
              <span
                class="affected-repositories__dot affected-repositories__dot--{{ repo.newStatus }}"
                aria-hidden="true"
              ></span>
              {{ _labelFor(repo.newStatus) }}
            </span>
            @if (reason) {
              <span class="affected-repositories__reason">{{ reason }}</span>
            }
          </li>
        }
      </ul>
    </div>
  `,
  styleUrl: './affected-repositories.scss',
})
export class AffectedRepositoriesComponent {
  readonly repositories: InputSignal<AffectedRepository[]> = input.required<AffectedRepository[]>();
  readonly dismiss: OutputEmitterRef<void> = output<void>();

  protected readonly headingId = PANEL_HEADING_ID;

  protected readonly _hasLostAccess = computed(() =>
    this.repositories().some(r => isLostAccess(r.newStatus as AffectedRepositoryStatus))
  );

  protected readonly _lostAccessCount = computed(() =>
    this.repositories().filter(r => isLostAccess(r.newStatus as AffectedRepositoryStatus)).length
  );

  protected readonly _sortedRepositories = computed(() => {
    const repos = [...this.repositories()];
    return repos.sort((a, b) => {
      const aLost = isLostAccess(a.newStatus as AffectedRepositoryStatus) ? 0 : 1;
      const bLost = isLostAccess(b.newStatus as AffectedRepositoryStatus) ? 0 : 1;
      return aLost - bLost;
    });
  });

  protected _labelFor(status: string): string {
    return affectedStatusLabel(status as AffectedRepositoryStatus);
  }

  protected reasonFor(status: string): string | null {
    if ((status as AffectedRepositoryStatus) === 'credential-unreadable') {
      return CREDENTIAL_UNREADABLE_REASON;
    }
    return null;
  }
}
