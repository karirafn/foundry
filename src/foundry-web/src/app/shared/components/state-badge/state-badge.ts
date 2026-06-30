import { ChangeDetectionStrategy, Component, input, InputSignal } from '@angular/core';
import { IssueState } from '../../../features/issues/issue.model';
import { STATE_LABELS, STATE_ARIA_LABELS, STATE_CSS_CLASSES } from '../../../features/issues/state-display';
import { getFailureCategoryDisplay } from '../../../features/workers/failure-category';

const FAILED_STATES: ReadonlySet<IssueState> = new Set<IssueState>(['failed', 'continuable_failed']);

@Component({
  selector: 'fd-state-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="badge"
      role="img"
      [class]="badgeClass()"
      [attr.aria-label]="ariaLabel()"
    >{{ label() }}</span>
  `,
  styleUrl: './state-badge.scss',
})
export class StateBadgeComponent {
  readonly state: InputSignal<IssueState> = input.required<IssueState>();
  readonly failureClassification: InputSignal<string | undefined> = input<string | undefined>(undefined);

  private failureCategoryDisplay() {
    const classification = this.failureClassification();
    if (classification === undefined || !FAILED_STATES.has(this.state())) {
      return null;
    }
    return getFailureCategoryDisplay(classification);
  }

  label(): string {
    const display = this.failureCategoryDisplay();
    return display !== null ? display.label : STATE_LABELS[this.state()];
  }

  ariaLabel(): string {
    const display = this.failureCategoryDisplay();
    return display !== null
      ? `State: ${display.label.toLowerCase()}`
      : `State: ${STATE_ARIA_LABELS[this.state()]}`;
  }

  badgeClass(): string {
    const display = this.failureCategoryDisplay();
    return display !== null
      ? `badge ${display.cssClass}`
      : `badge ${STATE_CSS_CLASSES[this.state()]}`;
  }
}
