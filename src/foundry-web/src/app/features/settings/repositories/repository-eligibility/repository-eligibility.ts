import { ChangeDetectionStrategy, Component, InputSignal, input } from '@angular/core';
import { EligibilityStatus, EligibilityViolation } from '../repository.model';

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

      @if (status() === 'ineligible' && violations().length > 0) {
        <ul class="repository-eligibility__violations" aria-label="Eligibility violations">
          @for (violation of violations(); track violation.rule) {
            <li class="repository-eligibility__violation">{{ violation.description }}</li>
          }
        </ul>
      }

      <span class="sr-only" aria-live="polite">{{ _statusLabel() }}</span>
    </div>
  `,
  styleUrl: './repository-eligibility.scss',
})
export class RepositoryEligibilityComponent {
  readonly status: InputSignal<EligibilityStatus> = input.required<EligibilityStatus>();
  readonly violations: InputSignal<EligibilityViolation[]> = input<EligibilityViolation[]>([]);
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
