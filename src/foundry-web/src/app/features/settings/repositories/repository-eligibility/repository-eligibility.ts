import { ChangeDetectionStrategy, Component, InputSignal, input } from '@angular/core';
import { EligibilityStatus, eligibilityStatusLabel } from '../repository.model';

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
        <span class="repository-eligibility__label">{{ _visibleLabel() }}</span>
      </div>
    </div>
  `,
  styleUrl: './repository-eligibility.scss',
})
export class RepositoryEligibilityComponent {
  readonly status: InputSignal<EligibilityStatus> = input.required<EligibilityStatus>();
  readonly recheckPending: InputSignal<boolean> = input<boolean>(false);

  _visibleLabel(): string {
    if (this.recheckPending()) {
      return 'Re-checking...';
    }
    return eligibilityStatusLabel(this.status());
  }
}
