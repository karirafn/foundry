import { Component, input, InputSignal } from '@angular/core';
import { IssueState } from '../../../features/issues/issue.model';

const STATE_LABELS: Record<IssueState, string> = {
  detected: 'DETECTED',
  queued: 'QUEUED',
  blocked: 'BLOCKED',
  in_progress: 'IN PROGRESS',
  review: 'REVIEW',
  unchanged: 'UNCHANGED',
  failed: 'FAILED',
  completed: 'COMPLETED',
  dismissed: 'DISMISSED',
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
  completed: 'completed',
  dismissed: 'dismissed',
  revision_queued: 'revision queued',
  revision_in_progress: 'revision in progress',
  revision_failed: 'revision failed',
  ineligible: 'ineligible',
};

const STATE_CSS_CLASSES: Record<IssueState, string> = {
  detected: 'badge--detected',
  queued: 'badge--queued',
  blocked: 'badge--blocked',
  in_progress: 'badge--in-progress',
  review: 'badge--review',
  unchanged: 'badge--unchanged',
  failed: 'badge--failed',
  completed: 'badge--completed',
  dismissed: 'badge--dismissed',
  revision_queued: 'badge--revision-queued',
  revision_in_progress: 'badge--revision-in-progress',
  revision_failed: 'badge--revision-failed',
  ineligible: 'badge--ineligible',
};

@Component({
  selector: 'fd-state-badge',
  standalone: true,
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

  label(): string {
    return STATE_LABELS[this.state()];
  }

  ariaLabel(): string {
    return `State: ${STATE_ARIA_LABELS[this.state()]}`;
  }

  badgeClass(): string {
    return `badge ${STATE_CSS_CLASSES[this.state()]}`;
  }
}
