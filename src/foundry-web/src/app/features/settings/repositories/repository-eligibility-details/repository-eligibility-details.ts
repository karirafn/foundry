import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, input, output } from '@angular/core';
import { EligibilityStatus, EligibilityViolation } from '../repository.model';

@Component({
  selector: 'fd-repository-eligibility-details',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="repository-eligibility-details" [id]="panelId()">
      <div class="repository-eligibility-details__header">
        <h3 class="repository-eligibility-details__heading">
          {{ _heading() }}
        </h3>
        <button
          class="repository-eligibility-details__recheck-btn"
          [class.repository-eligibility-details__recheck-btn--unreachable]="status() === 'unreachable'"
          type="button"
          [disabled]="recheckPending()"
          [attr.aria-label]="'Re-check eligibility'"
          (click)="recheck.emit()"
        >{{ recheckPending() ? 'Re-checking...' : 'Re-check' }}</button>
      </div>

      @if (status() === 'ineligible') {
        <ul class="repository-eligibility-details__violations" aria-label="Eligibility violations">
          @for (violation of violations(); track violation.rule) {
            <li class="repository-eligibility-details__violation">{{ violation.description }}</li>
          }
        </ul>
      }

      @if (status() === 'unreachable') {
        <p class="repository-eligibility-details__explanation">
          Foundry could not read branch-protection settings for this repository.
          Check the account's access, then re-check.
        </p>
      }

      <span
        class="repository-eligibility-details__recheck-error"
        role="alert"
        [attr.aria-hidden]="recheckError() === null"
      >{{ recheckError() ?? '' }}</span>
    </div>
  `,
  styleUrl: './repository-eligibility-details.scss',
})
export class RepositoryEligibilityDetailsComponent {
  readonly status: InputSignal<EligibilityStatus> = input.required<EligibilityStatus>();
  readonly violations: InputSignal<EligibilityViolation[]> = input<EligibilityViolation[]>([]);
  readonly recheckPending: InputSignal<boolean> = input<boolean>(false);
  readonly recheckError: InputSignal<string | null> = input<string | null>(null);
  readonly panelId: InputSignal<string> = input.required<string>();

  readonly recheck: OutputEmitterRef<void> = output<void>();

  _heading(): string {
    return this.status() === 'unreachable'
      ? 'Branch protection could not be verified'
      : 'Branch protection violations';
  }
}
