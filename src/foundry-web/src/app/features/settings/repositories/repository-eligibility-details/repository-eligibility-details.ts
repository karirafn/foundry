import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, input, output } from '@angular/core';
import { EligibilityReason, EligibilityStatus, EligibilityViolation } from '../repository.model';
import { providerDisplayName } from '../../../../shared/utils/provider.util';

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
          type="button"
          [disabled]="recheckPending() || reason() === 'rate-limited'"
          [attr.title]="reason() === 'rate-limited' ? _rateLimitTitle() : null"
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
          {{ _unreachableExplanation() }}
        </p>
      }

      <span
        class="repository-eligibility-details__recheck-error"
        [attr.aria-hidden]="recheckError() === null"
      >{{ recheckError() ?? '' }}</span>
    </div>
  `,
  styleUrl: './repository-eligibility-details.scss',
})
export class RepositoryEligibilityDetailsComponent {
  readonly status: InputSignal<EligibilityStatus> = input.required<EligibilityStatus>();
  readonly violations: InputSignal<EligibilityViolation[]> = input<EligibilityViolation[]>([]);
  readonly reason: InputSignal<EligibilityReason | null> = input<EligibilityReason | null>(null);
  readonly recheckPending: InputSignal<boolean> = input<boolean>(false);
  readonly recheckError: InputSignal<string | null> = input<string | null>(null);
  readonly panelId: InputSignal<string> = input.required<string>();
  readonly providerType: InputSignal<string> = input<string>('github');

  readonly recheck: OutputEmitterRef<void> = output<void>();

  _heading(): string {
    if (this.status() !== 'unreachable') {
      return "Why this repository can't be dispatched to";
    }
    switch (this.reason()) {
      case 'rate-limited':
        return `${providerDisplayName(this.providerType())} API rate limit reached`;
      case 'never-probed':
        return 'Eligibility not yet checked';
      case 'branch-rules-unavailable':
        return 'Branch protection could not be verified';
      default:
        return 'Branch protection could not be verified';
    }
  }

  _rateLimitTitle(): string {
    return `${providerDisplayName(this.providerType())} rate limit active — Foundry retries automatically`;
  }

  _unreachableExplanation(): string {
    switch (this.reason()) {
      case 'rate-limited':
        return `The ${providerDisplayName(this.providerType())} API rate limit has been reached. Foundry will retry automatically.`;
      case 'never-probed':
        return 'Eligibility has not been checked yet. Foundry will probe automatically.';
      case 'branch-rules-unavailable':
        return 'Foundry could not read branch-protection settings for this repository. Check the account\'s access, then re-check.';
      default:
        return 'Foundry could not read branch-protection settings for this repository. Check the account\'s access, then re-check.';
    }
  }
}
