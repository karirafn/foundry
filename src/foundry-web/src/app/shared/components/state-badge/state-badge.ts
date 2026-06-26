import { ChangeDetectionStrategy, Component, input, InputSignal } from '@angular/core';
import { IssueState } from '../../../features/issues/issue.model';
import { STATE_LABELS, STATE_ARIA_LABELS, STATE_CSS_CLASSES } from '../../../features/issues/state-display';

const USAGE_LIMITED = 'usage_limited';
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

  private isUsageLimited(): boolean {
    return this.failureClassification() === USAGE_LIMITED && FAILED_STATES.has(this.state());
  }

  label(): string {
    return this.isUsageLimited() ? 'USAGE LIMITED' : STATE_LABELS[this.state()];
  }

  ariaLabel(): string {
    return this.isUsageLimited()
      ? 'State: usage limited'
      : `State: ${STATE_ARIA_LABELS[this.state()]}`;
  }

  badgeClass(): string {
    return this.isUsageLimited()
      ? 'badge badge--usage-limited'
      : `badge ${STATE_CSS_CLASSES[this.state()]}`;
  }
}
