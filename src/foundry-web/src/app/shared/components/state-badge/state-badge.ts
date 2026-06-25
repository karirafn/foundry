import { ChangeDetectionStrategy, Component, input, InputSignal } from '@angular/core';
import { IssueState } from '../../../features/issues/issue.model';

const USAGE_LIMITED = 'usage_limited';
const FAILED_STATES: ReadonlySet<IssueState> = new Set<IssueState>(['failed', 'continuable_failed']);

const STATE_LABELS: Record<IssueState, string> = {
  detected: 'DETECTED',
  queued: 'QUEUED',
  blocked: 'BLOCKED',
  in_progress: 'IN PROGRESS',
  review: 'REVIEW',
  unchanged: 'UNCHANGED',
  failed: 'FAILED',
  continuable_failed: 'CONT FAILED',
  continuation_queued: 'CONT QUEUED',
  completed: 'COMPLETED',
  revision_queued: 'REV QUEUED',
  revision_in_progress: 'REV IN PROGRESS',
  revision_failed: 'REV FAILED',
  ineligible: 'INELIGIBLE',
};

const STATE_ARIA_LABELS: Record<IssueState, string> = {
  detected: 'detected',
  queued: 'queued',
  blocked: 'blocked',
  in_progress: 'in progress',
  review: 'review',
  unchanged: 'unchanged',
  failed: 'failed',
  continuable_failed: 'continuable failed',
  continuation_queued: 'continuation queued',
  completed: 'completed',
  revision_queued: 'revision queued',
  revision_in_progress: 'revision in progress',
  revision_failed: 'revision failed',
  ineligible: 'not eligible for dispatch',
};

const STATE_CSS_CLASSES: Record<IssueState, string> = {
  detected: 'badge--detected',
  queued: 'badge--queued',
  blocked: 'badge--blocked',
  in_progress: 'badge--in-progress',
  review: 'badge--review',
  unchanged: 'badge--unchanged',
  failed: 'badge--failed',
  continuable_failed: 'badge--continuable-failed',
  continuation_queued: 'badge--continuation-queued',
  completed: 'badge--completed',
  revision_queued: 'badge--revision-queued',
  revision_in_progress: 'badge--revision-in-progress',
  revision_failed: 'badge--revision-failed',
  ineligible: 'badge--ineligible',
};

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
