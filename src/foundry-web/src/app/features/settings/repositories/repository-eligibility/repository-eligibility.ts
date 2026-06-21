import { ChangeDetectionStrategy, Component, InputSignal, input } from '@angular/core';
import { EligibilityStatus } from '../repository.model';

@Component({
  selector: 'fd-repository-eligibility',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="repository-eligibility">
      <div class="repository-eligibility__status">
        <span
          class="repository-eligibility__indicator repository-eligibility__indicator--{{ status() }}"
          aria-hidden="true"
        ></span>
        <span class="repository-eligibility__label">{{ _statusLabel() }}</span>
      </div>

      <span class="sr-only" [attr.aria-live]="recheckPending() ? 'polite' : 'off'">{{ _statusLabel() }}</span>
    </div>
  `,
  styleUrl: './repository-eligibility.scss',
})
export class RepositoryEligibilityComponent {
  readonly status: InputSignal<EligibilityStatus> = input.required<EligibilityStatus>();
  readonly recheckPending: InputSignal<boolean> = input<boolean>(false);

  _statusLabel(): string {
    if (this.recheckPending()) {
      return 'Re-checking...';
    }
    switch (this.status()) {
      case 'eligible':
        return 'Eligible';
      case 'ineligible':
        return 'Ineligible';
      case 'unreachable':
        return 'Unable to verify branch protection';
    }
  }
}
